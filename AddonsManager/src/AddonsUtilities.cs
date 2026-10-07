using SwiftlyS2.Shared;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.SteamAPI;

namespace AddonsManager.Utils;

public class AddonsUtilities
{
    private ISwiftlyCore Core;
    private string CurrentWorkshopMap = string.Empty;
    private List<string> MountedAddons = [];
    private List<PublishedFileId_t> ImportantDownloads = [];
    private Queue<PublishedFileId_t> DownloadQueue = [];

    public AddonsUtilities(ISwiftlyCore core)
    {
        Core = core;
        core.Registrator.Register(this);
    }

    public List<string> StringToVector(string input)
    {
        if (string.IsNullOrEmpty(input)) return [];
        return input.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    public string VectorToString(List<string> vector)
    {
        return string.Join(",", vector);
    }

    public string BuildAddonPath(string addonName, bool legacy = false)
    {
        return $"{Core.GameFileSystem.GetSearchPath("EXECUTABLE_PATH", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 1)}steamapps/workshop/content/730/{addonName}/{addonName}{(legacy ? "" : "_dir")}.vpk";
    }

    public string GetCurrentWorkshopMap()
    {
        return CurrentWorkshopMap;
    }

    public void SetCurrentWorkshopMap(string mapName)
    {
        CurrentWorkshopMap = mapName;
    }

    public void ClearCurrentWorkshopMap()
    {
        SetCurrentWorkshopMap(string.Empty);
    }

    public List<string> GetMountedAddons()
    {
        return MountedAddons;
    }

    public Queue<PublishedFileId_t> GetDownloadQueue()
    {
        return DownloadQueue;
    }

    public List<PublishedFileId_t> GetImportantDownloads()
    {
        return ImportantDownloads;
    }

    // Callbacks can arrive out of order, so the finished addon isn't always the one waiting first
    public bool RemoveFromDownloadQueue(PublishedFileId_t addonId)
    {
        if (!DownloadQueue.Contains(addonId)) return false;

        var remaining = DownloadQueue.Where(id => id != addonId).ToList();
        DownloadQueue.Clear();
        foreach (var id in remaining) DownloadQueue.Enqueue(id);

        return true;
    }
}