using AddonsManager.Extensions;
using AddonsManager.Structs;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.SteamAPI;
using System.Runtime.InteropServices;

namespace AddonsManager;

public partial class AddonsManager
{
    public unsafe delegate void SetPendingHostStateRequestDelegate(nint hostStateManager, CHostStateRequest* pRequest);
    public delegate void ReplyConnection(nint server, nint client);

    private IUnmanagedFunction<SetPendingHostStateRequestDelegate>? _SetPendingHostStateRequestDelegate;
    private IUnmanagedFunction<ReplyConnection>? _ReplyConnection;

    public string WorkshopMapId = string.Empty;
    public Dictionary<ulong, ClientAddonInfo_t> ClientAddonInfo = new();

    public void SetupHooks()
    {
        _SetPendingHostStateRequestDelegate = core.Memory.GetUnmanagedFunctionByAddress<SetPendingHostStateRequestDelegate>(core.GameData.GetSignature("HostStateRequest"));
        _ReplyConnection = core.Memory.GetUnmanagedFunctionByAddress<ReplyConnection>(core.GameData.GetSignature("ReplyConnection"));

        _SetPendingHostStateRequestDelegate.AddHook((next) =>
        {
            unsafe
            {
                return (hostStateManager, pRequest) =>
                {
                    var kv = pRequest->pKV;
                    if (kv == null)
                    {
                        bool valveMap = core.GameFileSystem.FileExists($"maps/{pRequest->LevelName.Value}.vpk", "MOD");

                        if (pRequest->Addons.Value.Length > 0)
                        {
                            WorkshopMapId = pRequest->Addons.Value;
                        }
                        else if (valveMap)
                        {
                            WorkshopMapId = string.Empty;
                        }
                    }
                    else if (kv->GetName().ToLower() != "changelevel")
                    {
                        if (kv->GetName().ToLower() == "map_workshop")
                        {
                            WorkshopMapId = kv->GetString("customgamemode", "");
                        }
                        else
                        {
                            WorkshopMapId = string.Empty;
                        }
                    }

                    if (pRequest->Addons.Value.Length > 0 && core.GameFileSystem.IsDirectory(pRequest->Addons.Value, "OFFICIAL_ADDONS"))
                    {
                        WorkshopMapId = pRequest->Addons.Value;
                    }

                    if (Config.CurrentValue.Addons.Count == 0)
                    {
                        next()(hostStateManager, pRequest);
                        return;
                    }

                    var addons = Config.CurrentValue.Addons;
                    if (WorkshopMapId != string.Empty && !addons.Contains(WorkshopMapId))
                    {
                        addons = [WorkshopMapId, .. addons];
                    }

                    pRequest->Addons.Value = string.Join(',', addons);
                    next()(hostStateManager, pRequest);
                };
            }
        });

        _ReplyConnection.AddHook(next =>
        {
            return (pServer, pClient) =>
            {
                var steamId = pClient.AsRef<CSteamID>(core.GameData.GetOffset("CServerSideClient::SteamID"));
                ref var clientAddonInfo = ref GetClientAddonInfo(steamId.m_SteamID);

                if (
                    Config.CurrentValue.CacheClientsWithAddons && Config.CurrentValue.CacheClientsDurationInSeconds > 0.0f &&
                    DateTimeOffset.Now.ToUnixTimeMilliseconds() - clientAddonInfo.lastActiveTime > (long)(Config.CurrentValue.CacheClientsDurationInSeconds * 1000.0f)
                )
                {
                    clientAddonInfo.downloadedAddons = [];
                    clientAddonInfo.currentPendingAddon = string.Empty;
                }

                clientAddonInfo.lastActiveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

                var addonsPtr = pServer.AsRef<CUtlString>(core.GameData.GetOffset("NetworkGameServer::Addons"));
                var originalAddons = addonsPtr.Value;

                var clientAddons = GetClientAddons(steamId.m_SteamID);
                if (clientAddons.Count == 0)
                {
                    next()(pServer, pClient);
                    return;
                }

                if (!clientAddonInfo.downloadedAddons.Contains(clientAddons[0]))
                {
                    clientAddonInfo.currentPendingAddon = clientAddons[0];
                }

                addonsPtr.Value = string.Join(',', clientAddons);

                next()(pServer, pClient);

                addonsPtr.Value = originalAddons;
            };
        });
    }
    private ref ClientAddonInfo_t GetClientAddonInfo(ulong steamId)
    {
        ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(ClientAddonInfo, steamId, out bool exists);
        if (!exists)
        {
            info = new ClientAddonInfo_t
            {
                lastActiveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
                addonsToLoad = Config.CurrentValue.Addons,
                downloadedAddons = [],
                currentPendingAddon = string.Empty
            };
        }

        return ref info;
    }
}