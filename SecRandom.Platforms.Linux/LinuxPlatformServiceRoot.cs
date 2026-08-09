using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

public sealed class LinuxPlatformServiceRoot : IPlatformServiceRoot
{
    private static readonly bool IsX11Session = !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DISPLAY"));

    public PlatformKind Kind => PlatformKind.Linux;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.Linux,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: false,
        SupportsTopmost: IsX11Session,
        SupportsTaskSwitcherExclusion: IsX11Session,
        SupportsNoActivate: false,
        SupportsClickThrough: false,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: true,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; } = new LinuxWindowFeatureService();

    public IRemovableStorageCatalog RemovableStorage { get; } = new LinuxRemovableStorageCatalog();

    public IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; } =
        new LinuxRemovableStorageBindingMarker();
}
