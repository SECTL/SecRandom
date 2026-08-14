using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

public sealed class MacOsPlatformServiceRoot : IPlatformServiceRoot
{
    public PlatformKind Kind => PlatformKind.MacOs;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.MacOs,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: true,
        SupportsTopmost: true,
        SupportsTaskSwitcherExclusion: false,
        SupportsNoActivate: false,
        SupportsClickThrough: true,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: true,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; } = new MacOsWindowFeatureService();

    public IRemovableStorageCatalog RemovableStorage { get; } = new MacOsRemovableStorageCatalog();

    public IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; } =
        new MacOsRemovableStorageBindingMarker();

    public IPlatformCameraDeviceCatalog CameraDevices { get; } = new MacOsCameraDeviceCatalog();
}
