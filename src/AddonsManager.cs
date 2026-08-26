using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared;
using AddonsManager.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AddonsManager.Utils;
using AddonsManager.SteamWorkshop;
using AddonsManager.Commands;
using AddonsManager.Hooks;
using AddonsManager.Clients;

namespace AddonsManager;

[PluginMetadata(Id = "AddonsManager", Version = "2.0.3", Name = "Addons Manager", Author = "Swiftly Development Team", Description = "No description.")]
public class AddonsManager(ISwiftlyCore core) : BasePlugin(core)
{
    public IServiceProvider? ServiceProvider;
    private CancellationTokenSource? _downloadProgressTimer;

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
                .AddSingleton<AddonsUtilities>()
                .AddSingleton<AddonsWorkshopManager>()
                .AddSingleton<AddonsCommands>()
                .AddSingleton<AddonsClients>()
                .AddSingleton<AddonsHooks>()
                .AddOptionsWithValidateOnStart<AddonsConfig>()
                .BindConfiguration("Main");

        ServiceProvider = services.BuildServiceProvider();

        _ = ServiceProvider.GetRequiredService<AddonsUtilities>();
        var workshopManager = ServiceProvider.GetRequiredService<AddonsWorkshopManager>();
        _ = ServiceProvider.GetRequiredService<AddonsCommands>();
        _ = ServiceProvider.GetRequiredService<AddonsClients>();
        _ = ServiceProvider.GetRequiredService<AddonsHooks>();

        _downloadProgressTimer = Core.Scheduler.RepeatBySeconds(1.0f, workshopManager.PrintDownloadProgress);
    }

    public override void Unload()
    {
        // Stop the timer, then dispose (releases the Steam callback & mounted addons)
        try
        {
            _downloadProgressTimer?.Cancel();
            _downloadProgressTimer?.Dispose();
            _downloadProgressTimer = null;

            (ServiceProvider as IDisposable)?.Dispose();
            ServiceProvider = null;
        }
        catch (Exception ex)
        {
            Core.Logger.LogError(ex, "Unhandled exception while unloading AddonsManager.");
        }
    }
}
