using System.Collections.Generic;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.Services.Linkage;
using SecRandom.Services.Plugins;
using SecRandom.Services.Telemetry;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Desktop <see cref="IArchivePostImportHooks" />: refreshes desktop runtime services after an
///     import has been committed and re-synchronizes desktop integrations (autostart / URL
///     protocol) with the imported configuration, returning non-fatal warnings.
///     Core has already reloaded the main configuration and the configured profiles at this point.
/// </summary>
public sealed class DesktopArchivePostImportHooks(
    MainConfigHandler configHandler,
    DesktopIntegrationService desktopIntegrationService,
    DeviceUuidStore deviceUuidStore,
    IPluginManager pluginManager) : IArchivePostImportHooks
{
    public IReadOnlyList<string> OnSettingsImported()
    {
        return RefreshDesktopRuntime(refreshPlugins: false);
    }

    public IReadOnlyList<string> OnAllDataImported()
    {
        return RefreshDesktopRuntime(refreshPlugins: true);
    }

    private List<string> RefreshDesktopRuntime(bool refreshPlugins)
    {
        deviceUuidStore.Reload();
        IAppHost.TryGetService<IFeatureAvailabilityService>()?.Refresh();
        IAppHost.TryGetService<GlobalShortcutService>()?.Refresh();
        IAppHost.TryGetService<ShortcutService>()?.Refresh();
        _ = IAppHost.TryGetService<TelemetryRuntimeService>()?.RefreshAsync();
        IAppHost.TryGetService<OnlineStatusService>()?.Refresh();
        _ = IAppHost.TryGetService<CourseLinkageService>()?.RefreshAsync();
        if (refreshPlugins)
            pluginManager.Refresh();

        var warnings = new List<string>();
        desktopIntegrationService.EnsureConfiguredIntegrations();
        if (!configHandler.Data.General.Basic.Autostart && !desktopIntegrationService.TrySetAutostart(false, out var autostartError))
            warnings.Add($"未能移除开机启动注册：{autostartError}");
        if (!configHandler.Data.General.Basic.UrlProtocol && !desktopIntegrationService.TrySetUrlProtocol(false, out var protocolError))
            warnings.Add($"未能移除 URL 协议注册：{protocolError}");
        return warnings;
    }
}
