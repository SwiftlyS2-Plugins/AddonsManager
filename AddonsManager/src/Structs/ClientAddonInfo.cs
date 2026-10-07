namespace AddonsManager.Structs;

public class ClientAddonInfo
{
    public long LastActiveTime { get; set; }
    public List<string> DownloadedAddons { get; set; } = [];
    public string CurrentPendingAddon { get; set; } = string.Empty;
}
