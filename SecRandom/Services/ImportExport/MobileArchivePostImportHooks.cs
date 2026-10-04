using System.Collections.Generic;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.Config;
using SecRandom.Services.Linkage;
using SecRandom.Services.Telemetry;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     Mobile <see cref="IArchivePostImportHooks" />: refreshes the runtime services an imported
///     configuration can affect. Desktop integrations (autostart, URL protocol registration) have no
///     mobile equivalent, so this hook deliberately reports no warnings — the desktop implementation
///     is registered only in the desktop branch and never runs on a phone.
/// </summary>
public sealed class MobileArchivePostImportHooks(DeviceUuidStore deviceUuidStore) : IArchivePostImportHooks
{
    public IReadOnlyList<string> OnSettingsImported() => RefreshRuntime();

    public IReadOnlyList<string> OnAllDataImported() => RefreshRuntime();

    private IReadOnlyList<string> RefreshRuntime()
    {
        deviceUuidStore.Reload();
        IAppHost.TryGetService<IFeatureAvailabilityService>()?.Refresh();
        _ = IAppHost.TryGetService<TelemetryRuntimeService>()?.RefreshAsync();
        IAppHost.TryGetService<OnlineStatusService>()?.Refresh();
        _ = IAppHost.TryGetService<CourseLinkageService>()?.RefreshAsync();
        return [];
    }
}
