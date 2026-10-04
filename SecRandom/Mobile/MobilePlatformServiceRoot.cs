using SecRandom.Platforms.Abstractions;

namespace SecRandom.Mobile;

public sealed class MobilePlatformServiceRoot : IPlatformServiceRoot, IWindowFeatureService
{
    public MobilePlatformServiceRoot(PlatformKind kind)
    {
        Kind = kind;
        Capabilities = new PlatformCapabilities(
            Kind: kind,
            SupportsSingleView: true,
            SupportsMultipleWindows: false,
            SupportsWindowPositioning: false,
            SupportsTopmost: false,
            SupportsTaskSwitcherExclusion: false,
            SupportsNoActivate: false,
            SupportsClickThrough: false,
            SupportsCaptureExclusion: false,
            SupportsTrayIcon: false,
            SupportsGlobalShortcuts: false,
            SupportsUrlSchemeRegistration: false,
            SupportsUiAccess: false,
            SupportsBackgroundResidency: false);
    }

    public PlatformKind Kind { get; }

    /// <summary>
    /// Uses the desktop main navigation surface inside the mobile single-view host.
    /// iPadOS enables this because its larger layout can use the original main view.
    /// </summary>
    public bool UsesDesktopMainView { get; set; }

    public PlatformCapabilities Capabilities { get; }

    /// <summary>
    /// Platform heads inject their update installer here before PlatformStartupContext.Set runs.
    /// </summary>
    public IMobileUpdateInstaller UpdateInstaller { get; set; } = new UnsupportedMobileUpdateInstaller();

    /// <summary>
    /// Platform heads provide native local-media and TTS playback before startup.
    /// The neutral library uses the unsupported implementation for headless/test hosts.
    /// </summary>
    public IMobileMediaPlayer MediaPlayer { get; set; } = new UnsupportedMobileMediaPlayer();

    /// <summary>
    /// Platform heads may provide native keyboard occlusion notifications when Avalonia's input pane is unavailable.
    /// </summary>
    public IMobileKeyboardOcclusionSource? KeyboardOcclusionSource { get; set; }

    /// <summary>
    /// Platform heads may expose the mobile data root through a system file manager.
    /// </summary>
    public Func<string, bool>? PathLauncher { get; set; }

    /// <summary>
    ///     Platform heads provide the native system-browser opener (Android <c>ACTION_VIEW</c>,
    ///     iOS <c>UIApplication.OpenUrl</c>). Without it the shared launcher cannot open a link on a
    ///     mobile platform, which is also what the SECTL sign-in flow needs.
    /// </summary>
    public Func<string, bool>? UriLauncher { get; set; }

    /// <summary>
    ///     User-facing device label the platform head supplies (Android <c>Build.Model</c>, iOS
    ///     <c>UIDevice.Model</c>). Android and iOS report <c>localhost</c> as their host name, so
    ///     without this every phone would stamp the same cloud-backup device alias.
    /// </summary>
    public string? DeviceName { get; set; }

    /// <summary>
    ///     Platform heads may replace the authorization browser. iOS does, because
    ///     ASWebAuthenticationSession captures the custom-scheme redirect itself; every other head
    ///     keeps the shared launcher that opens the system browser and waits for the deep link.
    /// </summary>
    public Services.Auth.IAuthBrowser? AuthBrowser { get; set; }

    /// <summary>
    /// Platform heads may attach a startup error sink (for example Android.Util.Log) for Host-build failures.
    /// </summary>
    public Action<Exception>? StartupErrorLogger { get; set; }

    public IWindowFeatureService WindowFeatures => this;

    public IRemovableStorageCatalog RemovableStorage => UnsupportedRemovableStorageCatalog.Instance;

    public IRemovableStorageBindingMarker RemovableStorageBindingMarker =>
        PortableRemovableStorageBindingMarker.Instance;

    /// <summary>
    /// Platform heads may provide the system camera directory before startup.
    /// </summary>
    public IPlatformCameraDeviceCatalog CameraDevices { get; set; } =
        UnsupportedPlatformCameraDeviceCatalog.Instance;

    public global::SecRandom.Platforms.Abstractions.WindowFeatures SupportedFeatures =>
        global::SecRandom.Platforms.Abstractions.WindowFeatures.None;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request) =>
        WindowFeatureApplyResult.Unsupported(request.Features,
            "Mobile platforms do not expose desktop window features.");
}
