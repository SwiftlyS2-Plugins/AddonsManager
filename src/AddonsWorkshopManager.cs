using AddonsManager.Config;
using AddonsManager.Structs;
using AddonsManager.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.FileSystem;
using SwiftlyS2.Shared.SteamAPI;

namespace AddonsManager.SteamWorkshop;

public class AddonsWorkshopManager
{
    private ISwiftlyCore Core;
    private AddonsUtilities Utilities;
    private IOptionsMonitor<AddonsConfig> Config;
    private Callback<DownloadItemResult_t>? _downloadItemResult;

    public AddonsWorkshopManager(ISwiftlyCore core, AddonsUtilities utils, IOptionsMonitor<AddonsConfig> config)
    {
        Core = core;
        Utilities = utils;
        Config = config;
        core.Registrator.Register(this);
    }

    public bool MountAddon(string addonName, bool bAddToTail = false, bool bAllowRedownload = true)
    {
        if (addonName == string.Empty) return false;

        var alreadyMountedAddodns = Utilities.StringToVector(Utilities.GetCurrentWorkshopMap());
        if (alreadyMountedAddodns.Contains(addonName))
        {
            Core.Logger.LogWarning("Addon {AddonName} is already mounted by the server.", addonName);
            return false;
        }

        var iAddon = new PublishedFileId_t(ulong.TryParse(addonName, out var result) ? result : 0);
        var iAddonState = (EItemState)SteamGameServerUGC.GetItemState(iAddon);

        if (iAddonState.HasFlag(EItemState.k_EItemStateLegacyItem))
        {
            Core.Logger.LogError("Addon {AddonName} is not compatible with Source 2, skipping.", addonName);
            return false;
        }

        if (!iAddonState.HasFlag(EItemState.k_EItemStateInstalled))
        {
            Core.Logger.LogInformation("Addon {AddonName} is not installed, queuing for download.", addonName);
            DownloadAddon(addonName, true, true);
            return false;
        }
        else if (bAllowRedownload && Config.CurrentValue.RedownloadAddonOnMount)
        {
            DownloadAddon(addonName, false, true);
        }

        string addonPath = Utilities.BuildAddonPath(addonName);

        if (!Core.GameFileSystem.FileExists(addonPath, string.Empty))
        {
            addonPath = Utilities.BuildAddonPath(addonName, true);

            if (!Core.GameFileSystem.FileExists(addonPath, string.Empty))
            {
                Core.Logger.LogError("Addon {AddonName} couldn't be found at {AddonPath}.", addonName, addonPath);
                return false;
            }
        }
        else
        {
            // The filesystem API requires the base .vpk path (without _dir); it appends _dir and chunk suffixes internally
            addonPath = Utilities.BuildAddonPath(addonName, true);
        }

        if (Utilities.GetMountedAddons().Contains(addonName))
        {
            Core.Logger.LogError("Addon {AddonName} is already mounted.", addonName);
            return false;
        }

        Core.Logger.LogDebug("Mounting addon {AddonName} from path {AddonPath}.", addonName, addonPath);
        Core.GameFileSystem.AddSearchPath(addonPath, "GAME", bAddToTail ? SearchPathAdd_t.PATH_ADD_TO_TAIL : SearchPathAdd_t.PATH_ADD_TO_HEAD, SearchPathPriority_t.SEARCH_PATH_PRIORITY_VPK);
        Utilities.GetMountedAddons().Add(addonName);

        return true;
    }

    public bool UnmountAddon(string addonName)
    {
        if (addonName == string.Empty) return false;

        if (!Utilities.GetMountedAddons().Contains(addonName))
        {
            Core.Logger.LogError("Addon {AddonName} is not mounted.", addonName);
            return false;
        }

        string addonPath = Utilities.BuildAddonPath(addonName, true);

        if (!Core.GameFileSystem.RemoveSearchPath(addonPath, "GAME"))
            return false;

        Core.Logger.LogDebug("Unmounting addon {AddonName} from path {AddonPath}.", addonName, addonPath);
        Utilities.GetMountedAddons().Remove(addonName);

        return true;
    }

    public void PrintDownloadProgress()
    {
        var downloadQueue = Utilities.GetDownloadQueue();
        if (downloadQueue.Count == 0) return;

        if (!SteamGameServerUGC.GetItemDownloadInfo(downloadQueue.Peek(), out var bytesDownloaded, out var bytesTotal))
        {
            Core.Logger.LogError("Failed to get download info for addon {AddonName}.", downloadQueue.Peek());
            return;
        }

        float MBDownloaded = bytesDownloaded / (1024f * 1024f);
        float MBTotal = bytesTotal / (1024f * 1024f);

        float progress = bytesDownloaded / (float)bytesTotal * 100f;

        Core.Logger.LogDebug("Downloading addon {AddonName}: {MBDownloaded}/{MBTotal} MB ({Progress}%)", downloadQueue.Peek(), MBDownloaded.ToString("0.00"), MBTotal.ToString("0.00"), progress.ToString("0.00"));
    }

    public bool DownloadAddon(string pszAddon, bool bImportant, bool bForce)
    {
        var iAddon = new PublishedFileId_t(ulong.TryParse(pszAddon, out var result) ? result : 0);
        if (iAddon.m_PublishedFileId == 0)
        {
            Core.Logger.LogError("Invalid addon ID {AddonName}.", pszAddon);
            return false;
        }

        if (Utilities.GetDownloadQueue().Contains(iAddon))
        {
            Core.Logger.LogWarning("Addon {AddonName} is already in the download queue.", pszAddon);
            return false;
        }

        var iAddonState = (EItemState)SteamGameServerUGC.GetItemState(iAddon);

        if (!bForce && iAddonState.HasFlag(EItemState.k_EItemStateInstalled))
        {
            Core.Logger.LogInformation("Addon {AddonName} is already installed, skipping download.", pszAddon);
            return false;
        }

        if (!SteamGameServerUGC.DownloadItem(iAddon, false))
        {
            Core.Logger.LogError("Failed to start download for addon {AddonName} because the Addon ID is invalid or the server is not logged on Steam.", pszAddon);
            return false;
        }

        if (bImportant && !Utilities.GetImportantDownloads().Contains(iAddon))
        {
            Utilities.GetImportantDownloads().Add(iAddon);
        }

        Utilities.GetDownloadQueue().Enqueue(iAddon);
        Core.Logger.LogInformation("Started download for addon {AddonName}.", pszAddon);
        return true;
    }

    public void RefreshAddons(bool reloadMap = false)
    {
        Core.Logger.LogDebug("Refreshing addons ([green]{addonsList}[default], bReloadMap: {reloadMap}).", Utilities.VectorToString(Utilities.GetMountedAddons()), reloadMap);

        var mountedAddodns = new List<string>(Utilities.GetMountedAddons());
        foreach (var addon in mountedAddodns.Reverse<string>())
        {
            UnmountAddon(addon);
        }

        var bAllAddonsMounted = true;

        foreach (var addon in Config.CurrentValue.Addons)
        {
            if (!MountAddon(addon, bAllowRedownload: reloadMap))
            {
                bAllAddonsMounted = false;
                Core.Logger.LogError("Failed to mount addon {AddonName}.", addon);
            }
        }

        if (bAllAddonsMounted && reloadMap)
        {
            Core.Logger.LogInformation("All addons mounted successfully, reloading map.");
            ReloadMap();
        }
    }

    [EventListener<EventDelegates.OnSteamAPIActivated>]
    public void OnSteamAPIActivated()
    {
        _downloadItemResult = Callback<DownloadItemResult_t>.Create(OnAddonDownloaded);
        RefreshAddons(true);
    }

    public void ReloadMap()
    {
        if (Utilities.GetCurrentWorkshopMap().Length == 0 || Core.GameFileSystem.IsDirectory(Utilities.GetCurrentWorkshopMap(), "OFFICIAL_ADDONS"))
        {
            Core.Engine.ExecuteCommand("changelevel " + Core.Engine.GlobalVars.MapName.Value);
        }
        else
        {
            Core.Engine.ExecuteCommand("host_workshop_map " + Utilities.GetCurrentWorkshopMap());
        }
    }

    public void OnAddonDownloaded(DownloadItemResult_t pCallback)
    {
        // For now we also need to check for none since it's a SwiftlyS2 issue here (probably, not sure yet)
        if (pCallback.m_eResult == EResult.k_EResultOK || pCallback.m_eResult == EResult.k_EResultNone)
        {
            Core.Logger.LogInformation("Addon {AddonName} downloaded successfully.", pCallback.m_nPublishedFileId);
        }
        else
        {
            Core.Logger.LogError("Failed to download addon {AddonName}. Steam API returned result: {ResultCode}.\nError: {ErrorMessage}", pCallback.m_nPublishedFileId, pCallback.m_eResult, SteamErrorMessage.Errors[(int)pCallback.m_eResult]);
        }

        if (!Utilities.GetDownloadQueue().Contains(pCallback.m_nPublishedFileId))
        {
            Core.Logger.LogDebug("Received download result for addon {AddonName} which is not in the download queue, ignoring.", pCallback.m_nPublishedFileId);
            return;
        }

        Utilities.GetDownloadQueue().Dequeue();
        var bFound = Utilities.GetImportantDownloads().Remove(pCallback.m_nPublishedFileId);

        if (bFound && Utilities.GetImportantDownloads().Count == 0)
        {
            Core.Logger.LogInformation("All important addons have been downloaded, reloading map {mapName}.", Core.Engine.GlobalVars.MapName.Value);
            ReloadMap();
        }
    }
}