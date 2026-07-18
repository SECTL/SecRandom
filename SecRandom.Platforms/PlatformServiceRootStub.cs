using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms;

public sealed class PlatformServiceRootStub : IPlatformServiceRoot, IWindowFeatureService
{
    public static PlatformServiceRootStub Instance { get; } = new();

    private PlatformServiceRootStub()
    {
    }

    public PlatformKind Kind => PlatformKind.Unknown;

    public PlatformCapabilities Capabilities => PlatformCapabilities.Unsupported;

    public IWindowFeatureService WindowFeatures => this;

    public global::SecRandom.Platforms.Abstractions.WindowFeatures SupportedFeatures =>
        global::SecRandom.Platforms.Abstractions.WindowFeatures.None;

    public WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request) =>
        WindowFeatureApplyResult.Unsupported(request.Features, "The active platform does not support window features.");
}
