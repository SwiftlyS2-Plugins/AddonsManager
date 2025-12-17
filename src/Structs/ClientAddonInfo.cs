namespace AddonsManager.Structs;

public struct ClientAddonInfo_t
{
    public long lastActiveTime;
    public List<string> addonsToLoad;
    public List<string> downloadedAddons;
    public string currentPendingAddon;
}