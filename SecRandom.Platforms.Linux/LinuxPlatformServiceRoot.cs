using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

public sealed class LinuxPlatformServiceRoot : IPlatformServiceRoot
{
    private static readonly bool IsX11Session = !string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("DISPLAY")) && string.IsNullOrWhiteSpace(
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    public PlatformKind Kind => PlatformKind.Linux;

    public PlatformCapabilities Capabilities { get; } = new(
        Kind: PlatformKind.Linux,
        SupportsSingleView: false,
        SupportsMultipleWindows: true,
        SupportsWindowPositioning: false,
        SupportsTopmost: IsX11Session,
        SupportsTaskSwitcherExclusion: false,
        SupportsNoActivate: false,
        SupportsClickThrough: false,
        SupportsCaptureExclusion: false,
        SupportsTrayIcon: true,
        SupportsGlobalShortcuts: false,
        SupportsUrlSchemeRegistration: true,
        SupportsUiAccess: false,
        SupportsBackgroundResidency: true);

    public IWindowFeatureService WindowFeatures { get; } = new LinuxWindowFeatureService();
}
