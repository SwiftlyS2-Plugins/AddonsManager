using AddonsManager.Clients;
using AddonsManager.Config;
using AddonsManager.Extensions;
using AddonsManager.SteamWorkshop;
using AddonsManager.Structs;
using AddonsManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.NetMessages;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace AddonsManager.Hooks;

public class AddonsHooks
{
    private ISwiftlyCore Core;
    private AddonsWorkshopManager WorkshopManager;
    private AddonsClients Clients;
    private AddonsUtilities Utilities;

    public unsafe delegate void SetPendingHostStateRequestDelegate(nint hostStateManager, CHostStateRequest* pRequest);
    public delegate void ReplyConnection(nint server, nint client);
    public delegate ulong ScriptGetAddon();

    private IUnmanagedFunction<SetPendingHostStateRequestDelegate>? _SetPendingHostStateRequestDelegate;
    private IUnmanagedFunction<ReplyConnection>? _ReplyConnection;
    private IUnmanagedFunction<ScriptGetAddon>? _ScriptGetAddon;

    public AddonsHooks(ISwiftlyCore core, AddonsWorkshopManager workshopManager, AddonsClients clients, AddonsUtilities utils, IOptionsMonitor<AddonsConfig> config)
    {
        Core = core;
        WorkshopManager = workshopManager;
        Clients = clients;
        Utilities = utils;
        Core.Registrator.Register(this);

        _SetPendingHostStateRequestDelegate = core.Memory.GetUnmanagedFunctionByAddress<SetPendingHostStateRequestDelegate>(core.GameData.GetSignature("HostStateRequest"));
        _ReplyConnection = core.Memory.GetUnmanagedFunctionByAddress<ReplyConnection>(core.GameData.GetSignature("ReplyConnection"));
        _ScriptGetAddon = core.Memory.GetUnmanagedFunctionByAddress<ScriptGetAddon>(core.GameData.GetSignature("ScriptGetAddon"));

        _SetPendingHostStateRequestDelegate.AddHook((next) =>
        {
            unsafe
            {
                return (hostStateManager, pRequest) =>
                {
                    try
                    {
                        if (pRequest->m_pKV == null)
                        {
                            var bValveMap = Core.GameFileSystem.FileExists($"maps/{pRequest->m_LevelName.Value}.vpk", "MOD");

                            if (!string.IsNullOrEmpty(pRequest->m_Addons.Value))
                                Utilities.SetCurrentWorkshopMap(pRequest->m_Addons.Value);
                            else if (bValveMap)
                                Utilities.ClearCurrentWorkshopMap();
                        }
                        else if (!pRequest->m_pKV->GetName().Equals("changelevel", StringComparison.OrdinalIgnoreCase))
                        {
                            if (pRequest->m_pKV->GetName().Equals("map_workshop", StringComparison.OrdinalIgnoreCase))
                            {
                                var workshopMapId = pRequest->m_pKV->GetString("customgamemode", "");
                                // Only fall back to m_Addons.Value if it looks like a single workshop ID (no commas).
                                // Prevents corruption if the same CHostStateRequest struct is reused and m_Addons
                                // already contains a comma-separated list from a previous hook invocation.
                                if (string.IsNullOrEmpty(workshopMapId) && !pRequest->m_Addons.Value.Contains(','))
                                    workshopMapId = pRequest->m_Addons.Value;

                                if (!string.IsNullOrEmpty(workshopMapId))
                                    Utilities.SetCurrentWorkshopMap(workshopMapId);
                                else
                                    Utilities.ClearCurrentWorkshopMap();
                            }
                            else
                                Utilities.ClearCurrentWorkshopMap();
                        }
                        else
                        {
                            Utilities.ClearCurrentWorkshopMap();
                        }

                        if (!string.IsNullOrEmpty(pRequest->m_Addons.Value) && Core.GameFileSystem.IsDirectory(pRequest->m_Addons.Value, "OFFICIAL_ADDONS"))
                            Utilities.SetCurrentWorkshopMap(pRequest->m_Addons.Value);

                        if (Utilities.GetMountedAddons().Count == 0)
                        {
                            if (!string.IsNullOrEmpty(Utilities.GetCurrentWorkshopMap()))
                                pRequest->m_Addons = Utilities.GetCurrentWorkshopMap();
                        }
                        else if (string.IsNullOrEmpty(Utilities.GetCurrentWorkshopMap()))
                        {
                            pRequest->m_Addons = Utilities.VectorToString(Utilities.GetMountedAddons());
                        }
                        else
                        {
                            var newAddons = new List<string>(Utilities.GetMountedAddons());
                            newAddons.RemoveAll(a => a.Equals(Utilities.GetCurrentWorkshopMap(), StringComparison.OrdinalIgnoreCase));
                            newAddons.Insert(0, Utilities.GetCurrentWorkshopMap());
                            pRequest->m_Addons = Utilities.VectorToString(newAddons);
                        }
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.LogError(ex, "Unhandled exception in SetPendingHostStateRequest hook; forwarding request as-is.");
                    }

                    next()(hostStateManager, pRequest);
                };
            }
        });

        _ReplyConnection.AddHook((next) =>
        {
            unsafe
            {
                return (server, client) =>
                {
                    var nextCalled = false;
                    var addonsModified = false;
                    string? originalAddons = null;

                    try
                    {
                        var clientPtr = client;
                        var steamId64 = core.Memory.ToServerSideClient(clientPtr).SteamID.GetSteamID64();
                        var clientInfo = Clients.GetClientInfo(steamId64);

                        if (
                            config.CurrentValue.CacheClientsWithAddons && config.CurrentValue.CacheClientsDurationInSeconds > 0 &&
                            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clientInfo.LastActiveTime > config.CurrentValue.CacheClientsDurationInSeconds * 1000
                        )
                        {
                            Core.Logger.LogDebug("Client {SteamID} has not connected for a while, clearing the cache", steamId64);
                            clientInfo.CurrentPendingAddon = string.Empty;
                            clientInfo.DownloadedAddons.Clear();
                        }
                        clientInfo.LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                        ref var addons = ref server.AsRef<CUtlString>(core.GameData.GetOffset("NetworkGameServer::Addons"));
                        originalAddons = addons.Value;

                        var clientAddons = Clients.GetClientAddons();

                        if (clientAddons.Count == 0)
                        {
                            nextCalled = true;
                            next()(server, client);
                            return;
                        }

                        if (!clientInfo.DownloadedAddons.Contains(clientAddons.First()))
                            clientInfo.CurrentPendingAddon = clientAddons.First();

                        // Keep only already-downloaded addons plus the single current pending one.
                        // Sending undownloaded addons to the client causes CS2 to perform a signature
                        // check that fails instantly, crashing or disconnecting the client before it
                        // can be told to download anything (mirrors MultiAddonManager behaviour).
                        clientAddons.RemoveAll(a =>
                            !clientInfo.DownloadedAddons.Contains(a) &&
                            !a.Equals(clientInfo.CurrentPendingAddon, StringComparison.OrdinalIgnoreCase));

                        addons.Value = Utilities.VectorToString(clientAddons);
                        addonsModified = true;

                        Core.Logger.LogDebug("Sending addons {addons} to client {SteamID}", addons.Value, steamId64);
                        nextCalled = true;
                        next()(server, client);

                        addons.Value = originalAddons;
                        addonsModified = false;
                    }
                    catch (Exception ex)
                    {
                        Core.Logger.LogError(ex, "Unhandled exception in ReplyConnection hook; forwarding connection unmodified.");

                        if (addonsModified && originalAddons != null)
                        {
                            try
                            {
                                ref var addons = ref server.AsRef<CUtlString>(core.GameData.GetOffset("NetworkGameServer::Addons"));
                                addons.Value = originalAddons;
                            }
                            catch (Exception restoreEx)
                            {
                                Core.Logger.LogError(restoreEx, "Failed to restore server addons string after ReplyConnection hook error.");
                            }
                        }

                        if (!nextCalled)
                            next()(server, client);
                    }
                };
            }
        });

        _ScriptGetAddon.AddHook((next) =>
        {
            return () =>
            {
                try
                {
                    if (config.CurrentValue.Addons.Count == 0) return next()();

                    var iAddon = ulong.TryParse(Utilities.GetCurrentWorkshopMap(), out var workshopId) ? workshopId : 0;
                    if (iAddon == 0) return next()();

                    return iAddon;
                }
                catch (Exception ex)
                {
                    Core.Logger.LogError(ex, "Unhandled exception in ScriptGetAddon hook; falling back to original function.");
                    return next()();
                }
            };
        });
    }

    [EventListener<EventDelegates.OnStartupServer>]
    public void StartupServer()
    {
        try
        {
            Core.GameFileSystem.RemoveSearchPath("", "GAME");
            Core.GameFileSystem.RemoveSearchPath("", "DEFAULT_WRITE_PATH");

            // If CurrentWorkshopMap was cleared while the server was downloading the workshop map
            // (e.g. a null-KV SetPendingHostStateRequest fired with empty m_Addons + bValveMap==true
            // for the previous valve map), recover it from the level name before RefreshAddons runs
            // so that GetClientAddons() includes it and clients are prompted to download it.
            if (string.IsNullOrEmpty(Utilities.GetCurrentWorkshopMap()))
            {
                var mapName = Core.Engine.GlobalVars.MapName.Value;
                if (!string.IsNullOrEmpty(mapName))
                {
                    var parts = mapName.Split('/');
                    if (parts.Length >= 2 &&
                        parts[0].Equals("workshop", StringComparison.OrdinalIgnoreCase) &&
                        ulong.TryParse(parts[1], out _))
                    {
                        Core.Logger.LogDebug("Recovering CurrentWorkshopMap from map name: {MapId}", parts[1]);
                        Utilities.SetCurrentWorkshopMap(parts[1]);
                    }
                }
            }

            WorkshopManager.RefreshAddons();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in StartupServer handler.");
        }
    }

    [ServerNetMessageInternalHandler]
    public HookResult SendNetMessage(CNETMsg_SignonState signonState, int playerid)
    {
        try
        {
            var player = Core.PlayerManager.GetPlayer(playerid);
            ulong steamId;
            if (player == null)
            {
                var sid = Clients.GetSteamIdBySlot(playerid);
                if (sid == null) return HookResult.Continue;
                steamId = sid.Value;
            }
            else
            {
                steamId = player.UnauthorizedSteamID;
            }

            var clientInfo = Clients.GetClientInfo(steamId);
            clientInfo.LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var clientAddons = Clients.GetClientAddons();

            if (signonState.SignonState == SignonState_t.SIGNONSTATE_CHANGELEVEL)
            {
                var addonsList = Utilities.StringToVector(signonState.Addons);

                if (addonsList.Count > 1)
                {
                    signonState.Addons = addonsList.First();
                    clientInfo.CurrentPendingAddon = addonsList.First();
                }
                else if (addonsList.Count == 1)
                {
                    clientInfo.CurrentPendingAddon = signonState.Addons;
                }

                return HookResult.Continue;
            }

            foreach (var downloadedAddon in clientInfo.DownloadedAddons)
            {
                clientAddons.Remove(downloadedAddon);
            }

            if (clientAddons.Count == 0) return HookResult.Continue;

            Core.Logger.LogDebug("Client {SteamID} has pending addons: {Addons}", steamId, string.Join(", ", clientAddons));

            clientInfo.CurrentPendingAddon = clientAddons.First();
            signonState.Addons = clientAddons.First();
            signonState.SignonState = SignonState_t.SIGNONSTATE_CHANGELEVEL;

            return HookResult.Continue;
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in SendNetMessage handler for player {PlayerId}.", playerid);
            return HookResult.Continue;
        }
    }
}
