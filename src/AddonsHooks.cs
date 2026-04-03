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
    public unsafe delegate void ReplyConnection(nint server, CServerSideClient* client);
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
                    if (pRequest->m_pKV == null)
                    {
                        var bValveMap = Core.GameFileSystem.FileExists($"maps/{pRequest->m_LevelName.Value}.vpk", "MOD");

                        if (!string.IsNullOrEmpty(pRequest->m_Addons.Value))
                            Utilities.SetCurrentWorkshopMap(pRequest->m_Addons.Value);
                        else if (bValveMap)
                            Utilities.ClearCurrentWorkshopMap();
                    }
                    else if (!pRequest->m_pKV->GetName().Equals("changelevel", StringComparison.CurrentCultureIgnoreCase))
                    {
                        if (pRequest->m_pKV->GetName().Equals("map_workshop", StringComparison.CurrentCultureIgnoreCase))
                        {
                            // customgamemode holds the workshop map ID for host_workshop_map requests (matches MultiAddonManager behaviour).
                            // m_Addons.Value is used as a fallback in case the KV key is unavailable.
                            var workshopMapId = pRequest->m_pKV->GetString("customgamemode", "");
                            if (string.IsNullOrEmpty(workshopMapId))
                                workshopMapId = pRequest->m_Addons.Value;

                            if (!string.IsNullOrEmpty(workshopMapId))
                                Utilities.SetCurrentWorkshopMap(workshopMapId);
                            else
                                Utilities.ClearCurrentWorkshopMap();
                        }
                        else
                            Utilities.ClearCurrentWorkshopMap();
                    }

                    if (!string.IsNullOrEmpty(pRequest->m_Addons.Value) && Core.GameFileSystem.IsDirectory(pRequest->m_Addons.Value, "OFFICIAL_ADDONS"))
                        Utilities.SetCurrentWorkshopMap(pRequest->m_Addons.Value);

                    if (Utilities.GetMountedAddons().Count == 0)
                    {
                        next()(hostStateManager, pRequest);
                        return;
                    }

                    if (string.IsNullOrEmpty(Utilities.GetCurrentWorkshopMap()))
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
                    var steamId64 = core.Memory.ToServerSideClient((nint)client).SteamID.GetSteamID64();
                    var clientInfo = Clients.GetClientInfo((long)steamId64);
                    if (
                        config.CurrentValue.CacheClientsWithAddons && config.CurrentValue.CacheClientsDurationInSeconds > 0 &&
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clientInfo.LastActiveTime > config.CurrentValue.ExtraAddonsTimeoutInSeconds * 1000
                    )
                    {
                        Core.Logger.LogDebug("Client {SteamID} has not connected for a while, clearing the cache", steamId64);
                        clientInfo.CurrentPendingAddon = string.Empty;
                        clientInfo.DownloadedAddons.Clear();
                    }
                    clientInfo.LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    ref var addons = ref server.AsRef<CUtlString>(core.GameData.GetOffset("NetworkGameServer::Addons"));
                    var originalAddons = addons.Value;

                    var clientAddons = Clients.GetClientAddons();
                    if (clientAddons.Count == 0)
                    {
                        next()(server, client);
                        return;
                    }

                    if (!clientInfo.DownloadedAddons.Contains(clientAddons.First()))
                        clientInfo.CurrentPendingAddon = clientAddons.First();

                    var iAddonCount = clientAddons.Count;
                    if (!string.IsNullOrEmpty(clientInfo.CurrentPendingAddon) && iAddonCount > 1)
                    {
                        var iNextAddonIndex = clientAddons.IndexOf(clientInfo.CurrentPendingAddon) + 1;
                        if (iNextAddonIndex > 0 && iNextAddonIndex < clientAddons.Count)
                        {
                            clientAddons.RemoveRange(iNextAddonIndex, clientAddons.Count - iNextAddonIndex);
                        }
                    }

                    addons.Value = Utilities.VectorToString(clientAddons);

                    Core.Logger.LogDebug("Sending addons {addons} to client {SteamID}", addons.Value, steamId64);
                    next()(server, client);

                    addons.Value = originalAddons;
                };
            }
        });

        _ScriptGetAddon.AddHook((next) =>
        {
            return () =>
            {
                if (config.CurrentValue.Addons.Count == 0) return next()();

                var iAddon = ulong.TryParse(Utilities.GetCurrentWorkshopMap(), out var workshopId) ? workshopId : 0;
                if (iAddon == 0) return next()();

                return iAddon;
            };
        });
    }

    [EventListener<EventDelegates.OnStartupServer>]
    public void StartupServer()
    {
        Core.GameFileSystem.RemoveSearchPath("", "GAME");
        Core.GameFileSystem.RemoveSearchPath("", "DEFAULT_WRITE_PATH");

        WorkshopManager.RefreshAddons();
    }

    [ServerNetMessageInternalHandler]
    public HookResult SendNetMessage(CNETMsg_SignonState signonState, int playerid)
    {
        var player = Core.PlayerManager.GetPlayer(playerid);
        if (player == null) return HookResult.Continue;

        var clientInfo = Clients.GetClientInfo((long)player.UnauthorizedSteamID);
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

        Core.Logger.LogDebug("Client {SteamID} has pending addons: {Addons}", player.UnauthorizedSteamID, string.Join(", ", clientAddons));

        clientInfo.CurrentPendingAddon = clientAddons.First();
        signonState.Addons = clientAddons.First();
        signonState.SignonState = SignonState_t.SIGNONSTATE_CHANGELEVEL;

        return HookResult.Continue;
    }
}