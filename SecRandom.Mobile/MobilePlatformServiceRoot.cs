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

    public PlatformCapabilities Capabilities { get; }

    public IWindowFeatureService WindowFeatures => this;

    public global::SecRandom.Platforms.Abstractions.WindowFeatures SupportedFeatures =>
        global::SecRandom.Platforms.Abstractions.WindowFeatures.None;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request) =>
        WindowFeatureApplyResult.Unsupported(request.Features,
            "Mobile platforms do not expose desktop window features.");
}
