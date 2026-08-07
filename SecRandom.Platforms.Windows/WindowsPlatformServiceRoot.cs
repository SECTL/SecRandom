using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

public sealed class WindowsPlatformServiceRoot : IPlatformServiceRoot
{
    public WindowsPlatformServiceRoot()
    {
        WindowFeatures = new WindowsWindowFeatureService();
    }

    public PlatformKind Kind => PlatformKind.Windows;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.Windows,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: true,
        SupportsTopmost: true,
        SupportsTaskSwitcherExclusion: true,
        SupportsNoActivate: true,
        SupportsClickThrough: true,
        SupportsCaptureExclusion: OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
        SupportsTrayIcon: true,
        SupportsGlobalShortcuts: true,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: true,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; }

    public IRemovableStorageCatalog RemovableStorage { get; } = new WindowsRemovableStorageCatalog();

    public IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; } =
        new WindowsRemovableStorageBindingMarker();
}
