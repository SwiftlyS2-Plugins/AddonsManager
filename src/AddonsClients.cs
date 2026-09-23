using System.Collections.Concurrent;
using AddonsManager.Config;
using AddonsManager.Structs;
using AddonsManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace AddonsManager.Clients;

public class AddonsClients
{
    private ISwiftlyCore Core;
    private AddonsUtilities Utilities;
    private ConcurrentDictionary<ulong, ClientAddonInfo> Clients = [];
    private IOptionsMonitor<AddonsConfig> Config;
    // Persists slot→steamId across disconnects so SendNetMessage can find client state
    // even when the player is no longer in PlayerManager (e.g. map-change CHANGELEVEL phase).
    private ConcurrentDictionary<int, ulong> _slotToSteamId = [];

    public AddonsClients(ISwiftlyCore core, AddonsUtilities utils, IOptionsMonitor<AddonsConfig> config)
    {
        Core = core;
        Utilities = utils;
        Config = config;
        Core.Registrator.Register(this);
    }

    public ConcurrentDictionary<ulong, ClientAddonInfo> GetClients()
    {
        return Clients;
    }

    public ulong? GetSteamIdBySlot(int slot)
    {
        return _slotToSteamId.TryGetValue(slot, out var steamId) ? steamId : null;
    }

    public ClientAddonInfo GetClientInfo(ulong steamId)
    {
        return Clients.GetOrAdd(steamId, _ => new ClientAddonInfo
        {
            LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DownloadedAddons = [],
            CurrentPendingAddon = string.Empty
        });
    }

    public List<string> GetClientAddons()
    {
        var clientAddons = new List<string>();

        if (Utilities.GetCurrentWorkshopMap().Length > 0)
        {
            clientAddons.Add(Utilities.GetCurrentWorkshopMap());
        }

        clientAddons.AddRange(Utilities.GetMountedAddons());

        return clientAddons;
    }

    [EventListener<EventDelegates.OnClientConnected>]
    public void OnClientConnected(IOnClientConnectedEvent @event)
    {
        try
        {
            var addons = GetClientAddons();
            if (addons.Count == 0) return;

            var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
            if (player == null) return;

            var clientInfo = GetClientInfo(player.UnauthorizedSteamID);

            if (!string.IsNullOrEmpty(clientInfo.CurrentPendingAddon))
            {
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - clientInfo.LastActiveTime > Config.CurrentValue.ExtraAddonsTimeoutInSeconds * 1000)
                {
                    Core.Logger.LogDebug("Client {SteamID} has reconnected after the timeout or did not receive the addon message, will not add addon {addonId} to the downloaded list.", player.UnauthorizedSteamID, clientInfo.CurrentPendingAddon);
                }
                else
                {
                    Core.Logger.LogDebug("Client {SteamID} has connected within the interval with the pending addon {addonId}, will send next addon in SendNetMessage hook.", player.UnauthorizedSteamID, clientInfo.CurrentPendingAddon);
                    clientInfo.DownloadedAddons.Add(clientInfo.CurrentPendingAddon);
                }

                clientInfo.CurrentPendingAddon = string.Empty;
            }

            clientInfo.LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in OnClientConnected handler for player {PlayerId}.", @event.PlayerId);
        }
    }

    [EventListener<EventDelegates.OnClientDisconnected>]
    public void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        try
        {
            var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
            if (player == null) return;

            var clientInfo = GetClientInfo(player.UnauthorizedSteamID);
            // Store slot→steamId so SendNetMessage can find the client after PM removal.
            _slotToSteamId[@event.PlayerId] = player.UnauthorizedSteamID;
            clientInfo.LastActiveTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in OnClientDisconnected handler for player {PlayerId}.", @event.PlayerId);
        }
    }

    [EventListener<EventDelegates.OnClientPutInServer>]
    public void OnClientPutInServer(IOnClientPutInServerEvent @event)
    {
        try
        {
            var player = Core.PlayerManager.GetPlayer(@event.PlayerId);
            if (player == null) return;

            var clientInfo = GetClientInfo(player.UnauthorizedSteamID);
            Core.Logger.LogDebug("[OnClientPutInServer] steamId={SteamID} cacheEnabled={Cache} downloadedAddons=[{Downloaded}]",
                player.UnauthorizedSteamID, Config.CurrentValue.CacheClientsWithAddons, string.Join(",", clientInfo.DownloadedAddons));

            if (Config.CurrentValue.CacheClientsWithAddons) return;

            clientInfo.DownloadedAddons.Clear();
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in OnClientPutInServer handler for player {PlayerId}.", @event.PlayerId);
        }
    }

    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
    {
        try
        {
            if (Config.CurrentValue.BlockDisconnectMessages == false) return HookResult.Continue;

            if (@event.Reason == (short)ENetworkDisconnectionReason.NETWORK_DISCONNECT_LOOPSHUTDOWN) return HookResult.Stop;
            return HookResult.Continue;
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception in OnPlayerDisconnect handler.");
            return HookResult.Continue;
        }
    }
}
