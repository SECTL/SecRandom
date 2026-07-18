namespace SecRandom.Platforms.Abstractions;

public interface IWindowFeatureService
{
    WindowFeatures SupportedFeatures { get; }

    WindowFeatureApplyResult Apply(PlatformWindowHandle window, WindowFeatureRequest request);
}
