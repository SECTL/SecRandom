namespace SecRandom.Platforms.Abstractions;

public sealed record PlatformCapabilities(
    PlatformKind Kind,
    bool SupportsSingleView,
    bool SupportsMultipleWindows,
    bool SupportsWindowPositioning,
    bool SupportsTopmost,
    bool SupportsTaskSwitcherExclusion,
    bool SupportsNoActivate,
    bool SupportsClickThrough,
    bool SupportsCaptureExclusion,
    bool SupportsTrayIcon,
    bool SupportsGlobalShortcuts,
    bool SupportsUrlSchemeRegistration,
    bool SupportsUiAccess,
    bool SupportsBackgroundResidency)
{
    public static PlatformCapabilities Unsupported { get; } = new(
        PlatformKind.Unknown,
        SupportsSingleView: false,
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
