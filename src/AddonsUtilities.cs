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

    // Resolves the VPK a workshop item actually shipped, rather than assuming its name and location.
    //
    // Two assumptions in the old one-line form do not hold everywhere:
    //
    //   1. The VPK is not always <id>_dir.vpk. Steam names a published item's VPK after the
    //      source_folder recorded in its publish_data.txt, so an item published from a folder named
    //      "my_addon" downloads as my_addon_dir.vpk and is never found.
    //   2. EXECUTABLE_PATH is the binary folder (game/bin/linuxsteamrt64), and steamapps/ sits at the
    //      install root - a different distance up on a LinuxGSM layout than on a plain SteamCMD one.
    //
    // So walk up from each anchor the SDK exposes until the item's folder appears, and take whatever
    // VPK is in it. The legacy form (no _dir) is what MountAddon hands to AddSearchPath, because the
    // filesystem appends _dir and the numbered chunks itself.
    public string BuildAddonPath(string addonName, bool legacy = false)
    {
        var found = FindAddonVpk(addonName);

        if (found.Length > 0)
        {
            return legacy ? StripDirSuffix(found) : found;
        }

        // Nothing on disk yet - still return a constructed path so the caller's "couldn't be found at
        // X" error names a candidate instead of an empty string.
        var fallbackRoot = Core.GameFileSystem.GetSearchPath("EXECUTABLE_PATH", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 1);
        return $"{fallbackRoot}steamapps/workshop/content/730/{addonName}/{addonName}{(legacy ? "" : "_dir")}.vpk";
    }

    private string FindAddonVpk(string addonName)
    {
        foreach (var root in GetWorkshopRoots())
        {
            var folder = Path.Combine(root, "steamapps", "workshop", "content", "730", addonName);

            if (!Directory.Exists(folder))
            {
                continue;
            }

            var dirVpk = Path.Combine(folder, $"{addonName}_dir.vpk");
            if (File.Exists(dirVpk)) return dirVpk;

            var flatVpk = Path.Combine(folder, $"{addonName}.vpk");
            if (File.Exists(flatVpk)) return flatVpk;

            var any = Directory.EnumerateFiles(folder, "*_dir.vpk").FirstOrDefault()
                   ?? Directory.EnumerateFiles(folder, "*.vpk").FirstOrDefault();

            if (any != null) return any;
        }

        return string.Empty;
    }

    private IEnumerable<string> GetWorkshopRoots()
    {
        var anchors = new List<string>
        {
            Core.GameFileSystem.GetSearchPath("EXECUTABLE_PATH", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 1),
            Core.GameDirectory,
            Core.GameFilesDirectory,
        };

        var seen = new HashSet<string>();

        foreach (var anchor in anchors)
        {
            if (string.IsNullOrWhiteSpace(anchor)) continue;

            var dir = anchor.Trim().TrimEnd(';');

            for (var depth = 0; depth < 5 && dir.Length > 1; depth++)
            {
                if (seen.Add(dir)) yield return dir;

                var parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
                if (string.IsNullOrEmpty(parent)) break;

                dir = parent;
            }
        }
    }

    private static string StripDirSuffix(string vpkPath)
    {
        var dir = Path.GetDirectoryName(vpkPath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(vpkPath);

        if (stem.EndsWith("_dir", StringComparison.OrdinalIgnoreCase))
        {
            stem = stem[..^"_dir".Length];
        }

        return Path.Combine(dir, $"{stem}.vpk");
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
}