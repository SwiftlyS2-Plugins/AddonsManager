using System.Collections.Concurrent;
using AddonsManager.Structs;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.SteamAPI;

namespace AddonsManager;

public partial class AddonsManager
{
    public ConcurrentQueue<PublishedFileId_t> _downloadingAddons = new();
    public ConcurrentDictionary<PublishedFileId_t, bool> _importantAddons = new();
    public ConcurrentDictionary<PublishedFileId_t, DownloadInfo> _downloadInfo = new();

    public string BuildAddonPath(string addonId, bool legacy = false)
    {
        return $"{Core.GameFileSystem.GetSearchPath("EXECUTABLE_PATH", GetSearchPathTypes_t.GET_SEARCH_PATH_ALL, 1)}steamapps/workshop/content/730/{addonId}/{addonId}{(legacy ? "_dir" : string.Empty)}.vpk";
    }

    public bool UnmountAddon(string addonId)
    {
        if (!Core.GameFileSystem.RemoveSearchPath(BuildAddonPath(addonId), "GAME")) return false;

        MountedAddons.Remove(addonId);
        return true;
    }

    public bool DownloadAddon(string addonId, bool important, bool force)
    {
        var publishedAddon = new PublishedFileId_t(ulong.TryParse(addonId, out ulong id) ? id : 0);

        if (publishedAddon.m_PublishedFileId == 0)
        {
            Core.Logger.LogWarning($"Invalid addon ID: {addonId}");
            return false;
        }

        if (_downloadingAddons.Contains(publishedAddon))
        {
            Core.Logger.LogInformation($"Addon with ID: {addonId} is already being downloaded.");
            return false;
        }

        var itemState = SteamGameServerUGC.GetItemState(publishedAddon);

        if (!force && (itemState & (uint)EItemState.k_EItemStateInstalled) != 0)
        {
            Core.Logger.LogInformation($"Addon with ID: {addonId} is already installed.");
            return true;
        }

        if (!SteamGameServerUGC.DownloadItem(publishedAddon, false))
        {
            Core.Logger.LogWarning($"Failed to initiate download for addon with ID: {addonId}");
            return false;
        }

        if (important && !_importantAddons.ContainsKey(publishedAddon))
        {
            _importantAddons.TryAdd(publishedAddon, true);
        }

        _downloadingAddons.Enqueue(publishedAddon);
        Core.Logger.LogInformation($"Started downloading addon with ID: {addonId}");

        return true;
    }

    public bool MountAddon(string addonId, bool addToTail = false)
    {
        var serverMountedAddons = WorkshopMapId.Split(',');
        if (serverMountedAddons.Contains(addonId))
        {
            Core.Logger.LogInformation($"Addon with ID: {addonId} is already mounted by the server.");
            return false;
        }

        var publishedAddon = new PublishedFileId_t(ulong.TryParse(addonId, out ulong id) ? id : 0);
        var itemState = SteamGameServerUGC.GetItemState(publishedAddon);

        if ((itemState & (uint)EItemState.k_EItemStateLegacyItem) != 0)
        {
            Core.Logger.LogWarning($"Addon with ID: {addonId} is a legacy item and it's not compatible with Source 2");
            return false;
        }

        if ((itemState & (uint)EItemState.k_EItemStateInstalled) == 0)
        {
            Core.Logger.LogWarning($"Addon with ID: {addonId} is not installed. Downloading it now.");
            DownloadAddon(addonId, true, true);
            return false;
        }

        var vpkPath = BuildAddonPath(addonId);
        if (!Core.GameFileSystem.FileExists(vpkPath, string.Empty))
        {
            vpkPath = BuildAddonPath(addonId, true);
            if (!Core.GameFileSystem.FileExists(vpkPath, string.Empty))
            {
                Core.Logger.LogWarning($"Addon with ID: {addonId} is installed but the VPK file could not be found at path {vpkPath}.");
                return false;
            }
        }
        else
        {
            vpkPath = BuildAddonPath(addonId, true);
        }

        if (MountedAddons.Contains(addonId))
        {
            Core.Logger.LogWarning($"Addon with ID: {addonId} is already mounted.");
            return false;
        }

        Core.Logger.LogInformation($"Mounting addon with ID: {addonId} from path: {vpkPath}");

        Core.GameFileSystem.AddSearchPath(vpkPath, "GAME", addToTail ? SearchPathAdd_t.PATH_ADD_TO_TAIL : SearchPathAdd_t.PATH_ADD_TO_HEAD, SearchPathPriority_t.SEARCH_PATH_PRIORITY_VPK);
        MountedAddons.Add(addonId);

        return true;
    }

    public void RefreshAddons(bool reloadMap = false)
    {
        if (!SteamAPIInitialized) return;

        Core.Logger.LogInformation($"Refreshing addons list ({string.Join(',', Config.CurrentValue.Addons)}). Reload Map: {reloadMap}");

        foreach (string addonId in MountedAddons.ToArray().Reverse())
        {
            UnmountAddon(addonId);
        }

        foreach (string addonId in Config.CurrentValue.Addons)
        {
            if (!MountAddon(addonId))
            {
                Core.Logger.LogWarning($"Failed to mount addon with ID: {addonId}");
            }
        }

        if (reloadMap) ReloadMap();
    }

    public void ReloadMap()
    {
        if (WorkshopMapId.Length == 0 || Core.GameFileSystem.IsDirectory(WorkshopMapId, "OFFICIAL_ADDONS"))
        {
            Core.Engine.ExecuteCommand("map " + Core.Engine.GlobalVars.MapName.Value);
        }
        else
        {
            Core.Engine.ExecuteCommand("host_workshop_map " + WorkshopMapId);
        }
    }

    public void OnDownloadItemResult(DownloadItemResult_t pResult)
    {
        DisplayItemDownloaded(pResult.m_nPublishedFileId, pResult.m_eResult);
    }

    private void DisplayItemDownloaded(PublishedFileId_t itemId, EResult result)
    {
        if (!_downloadingAddons.Contains(itemId)) return;

        if (result == EResult.k_EResultOK)
        {
            Core.Logger.LogInformation($"Successfully downloaded addon with ID: {itemId.m_PublishedFileId}");
        }
        else
        {
            Core.Logger.LogError($"Failed to download addon with ID: {itemId.m_PublishedFileId}. Result: {result}.\nError: {SteamErrorMessage.Errors[(int)result]}");
        }

        _downloadingAddons.TryDequeue(out _);
        _downloadInfo.TryRemove(itemId, out _);
        bool found = _importantAddons.TryRemove(itemId, out _);

        if (found && _importantAddons.Count == 0)
        {
            Core.Logger.LogInformation("All important addons have been downloaded. Reloading map...");
            ReloadMap();
        }
    }

    public void ShowDownloadProgress()
    {
        if (_downloadingAddons.Count == 0) return;

        _downloadingAddons.TryPeek(out var downloadingCurrentAddon);

        if (!SteamGameServerUGC.GetItemDownloadInfo(downloadingCurrentAddon, out ulong bytesDownloaded, out ulong bytesTotal) || bytesTotal == 0)
        {
            _downloadInfo.TryRemove(downloadingCurrentAddon, out _);
            Core.Logger.LogInformation($"Downloading addon {downloadingCurrentAddon.m_PublishedFileId}: Unable to get download info.");
            return;
        }

        if (!_downloadInfo.ContainsKey(downloadingCurrentAddon))
        {
            _downloadInfo.TryAdd(downloadingCurrentAddon, new DownloadInfo()
            {
                bytesNow = bytesDownloaded,
                totalBytes = bytesTotal,
                elapsedTimeMilliseconds = 0,
                timestampMilliseconds = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds()
            });

            float progress = 0.0f;
            Core.Logger.LogInformation(
                $"Downloading addon {downloadingCurrentAddon.m_PublishedFileId}: {progress:F2}% (0.00/{bytesTotal / (1024 * 1024)} MB)\n" +
                $"[0.00s] {GenerateProgressBar(progress)} 0.00B/s"
            );
            return;
        }
        else
        {
            var info = _downloadInfo[downloadingCurrentAddon];
            ulong currentTimestamp = (ulong)DateTimeOffset.Now.ToUnixTimeMilliseconds();
            ulong timeDiff = currentTimestamp - info.timestampMilliseconds;
            if (timeDiff == 0) return;

            info.elapsedTimeMilliseconds += timeDiff;

            float progress = bytesDownloaded / (float)bytesTotal * 100.0f;

            ulong bytesDiff = bytesDownloaded - info.bytesNow;
            var speedInBytes = bytesDiff / (timeDiff / 1000.0f);
            var speedInKbps = speedInBytes / 1024.0f;
            var speedInMbps = speedInKbps / 1024.0f;

            var unit = "B/s";
            float speed;

            if (speedInMbps > 1.0f)
            {
                unit = "MB/s";
                speed = speedInMbps;
            }
            else if (speedInKbps > 1.0f)
            {
                unit = "KB/s";
                speed = speedInKbps;
            }
            else
            {
                speed = speedInBytes;
            }

            Core.Logger.LogInformation(
                $"Downloading addon {downloadingCurrentAddon.m_PublishedFileId}: {progress:F2}% ({bytesDownloaded / (1024 * 1024):F2}/{bytesTotal / (1024 * 1024)} MB)\n" +
                $"[{info.elapsedTimeMilliseconds / 1000.0f:F2}s] {GenerateProgressBar(progress)} {speed:F2}{unit}"
            );


            info.bytesNow = bytesDownloaded;
            info.timestampMilliseconds = currentTimestamp;
            _downloadInfo[downloadingCurrentAddon] = info;

            if (progress >= 100.0f)
            {
                DisplayItemDownloaded(downloadingCurrentAddon, EResult.k_EResultOK);
            }
        }
    }

    public List<string> GetClientAddons(ulong steamId)
    {
        List<string> output = [];
        if (WorkshopMapId.Length > 0)
        {
            output.Add(WorkshopMapId);
        }

        output.AddRange(MountedAddons);

        if (steamId != 0)
        {
            if (ClientAddonInfo.TryGetValue(steamId, out var info))
            {
                foreach (var addon in info.addonsToLoad)
                {
                    if (!output.Contains(addon))
                    {
                        output.Add(addon);
                    }
                }
            }
        }

        return output;
    }
}