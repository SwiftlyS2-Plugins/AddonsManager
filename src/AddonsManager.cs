using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;
using AddonsManager.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SteamAPI;
using SwiftlyS2.Shared.Commands;

namespace AddonsManager;

[PluginMetadata(Id = "AddonsManager", Version = "1.0.0", Name = "Addons Manager", Author = "Swiftly Development Team", Description = "No description.")]
public partial class AddonsManager(ISwiftlyCore core) : BasePlugin(core)
{
    public static IOptionsMonitor<AddonsConfig> Config { get; private set; } = null!;
    public static List<string> MountedAddons = [];
    public static Task? DisplayTask = null;
    public static bool SteamAPIInitialized = false;

    public override void ConfigureSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void UseSharedInterface(IInterfaceManager interfaceManager)
    {
    }

    public override void Load(bool hotReload)
    {
        Core.Configuration
            .InitializeJsonWithModel<AddonsConfig>("config.jsonc", "Main")
            .Configure(builder =>
            {
                builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true);
            });

        ServiceCollection services = new();
        services.AddSwiftly(Core)
            .AddOptions<AddonsConfig>()
            .BindConfiguration("Main");

        var provider = services.BuildServiceProvider();
        Config = provider.GetRequiredService<IOptionsMonitor<AddonsConfig>>();

        Core.Scheduler.RepeatBySeconds(0.1f, ShowDownloadProgress);

        DisplayTask = Task.Run(async () =>
        {
            while (true)
            {
                if (DisplayTask!.IsCanceled || DisplayTask.IsCompleted) return;

                ShowDownloadProgress();
                await Task.Delay(100);
            }
        });

        SetupHooks();
    }

    public override void Unload()
    {
        DisplayTask?.Dispose();
        DisplayTask = null;
    }

    [EventListener<EventDelegates.OnStartupServer>]
    public void OnStartupServer()
    {
        Core.GameFileSystem.RemoveSearchPath("", "GAME");
        Core.GameFileSystem.RemoveSearchPath("", "DEFAULT_WRITE_PATH");

        RefreshAddons();
    }

    private Callback<DownloadItemResult_t>? _downloadItemResult;

    [EventListener<EventDelegates.OnSteamAPIActivated>]
    public void OnSteamAPIActivated()
    {
        SteamAPIInitialized = true;

        _downloadItemResult = Callback<DownloadItemResult_t>.Create(OnDownloadItemResult);
        RefreshAddons(true);
    }

    [Command("searchpath")]
    public void ViewSearchPaths(ICommandContext _)
    {
        Core.GameFileSystem.PrintSearchPaths();
    }
}