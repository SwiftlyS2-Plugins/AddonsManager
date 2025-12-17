using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.NetMessages;
using SwiftlyS2.Shared.ProtobufDefinitions;

namespace AddonsManager;

public partial class AddonsManager
{
    [GameEventHandler(HookMode.Pre)]
    public HookResult OnPlayerDisconnect(EventPlayerDisconnect @event)
    {
        if (Config.CurrentValue.BlockDisconnectMessages == false) return HookResult.Continue;

        if (@event.Reason == (short)ENetworkDisconnectionReason.NETWORK_DISCONNECT_LOOPSHUTDOWN) return HookResult.Stop;
        return HookResult.Continue;
    }

    [ServerNetMessageInternalHandler]
    public HookResult OnSignonStateMessage(CNETMsg_SignonState @event, int playerid)
    {
        var player = core.PlayerManager.GetPlayer(playerid);
        if (player == null) return HookResult.Continue;

        ref var clientAddonInfo = ref GetClientAddonInfo(player.UnauthorizedSteamID);

        clientAddonInfo.lastActiveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        var clientAddons = GetClientAddons(player.UnauthorizedSteamID);

        if (@event.SignonState == SignonState_t.SIGNONSTATE_CHANGELEVEL)
        {
            var addons = @event.Addons.Split(',');
            if (addons.Length > 1)
            {
                @event.Addons = addons[0];
                clientAddonInfo.currentPendingAddon = addons[0];
            }
            else if (addons.Length == 1)
            {
                clientAddonInfo.currentPendingAddon = addons[0];
            }

            return HookResult.Continue;
        }

        foreach (var downloadedAddon in clientAddonInfo.downloadedAddons)
        {
            clientAddons.Remove(downloadedAddon);
        }

        if (clientAddons.Count == 0) return HookResult.Continue;

        clientAddonInfo.currentPendingAddon = clientAddons[0];
        @event.Addons = clientAddons[0];
        @event.SignonState = SignonState_t.SIGNONSTATE_CHANGELEVEL;

        return HookResult.Continue;
    }

    [EventListener<EventDelegates.OnClientConnected>]
    public void OnClientConnected(IOnClientConnectedEvent @event)
    {
        var player = core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null) return;

        var clientAddons = GetClientAddons(player.UnauthorizedSteamID);
        if (clientAddons.Count == 0) return;

        ref var clientAddonInfo = ref GetClientAddonInfo(player.UnauthorizedSteamID);

        if (clientAddonInfo.currentPendingAddon.Length > 0)
        {
            if (DateTimeOffset.Now.ToUnixTimeMilliseconds() - clientAddonInfo.lastActiveTime <= (long)(Config.CurrentValue.ExtraAddonsTimeoutInSeconds * 1000.0f))
            {
                clientAddonInfo.downloadedAddons.Add(clientAddonInfo.currentPendingAddon);
            }

            clientAddonInfo.currentPendingAddon = "";
        }

        clientAddonInfo.lastActiveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

    [EventListener<EventDelegates.OnClientDisconnected>]
    public void OnClientDisconnected(IOnClientDisconnectedEvent @event)
    {
        var player = core.PlayerManager.GetPlayer(@event.PlayerId);
        if (player == null) return;

        ref var clientAddonInfo = ref GetClientAddonInfo(player.SteamID);
        clientAddonInfo.lastActiveTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }
}