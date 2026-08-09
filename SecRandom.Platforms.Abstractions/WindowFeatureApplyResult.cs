namespace SecRandom.Platforms.Abstractions;

public enum WindowFeatureApplyStatus
{
    Applied,
    Unsupported,
    Failed
}

public readonly record struct WindowFeatureApplyResult(
    WindowFeatures AppliedFeatures,
    WindowFeatures UnsupportedFeatures,
    WindowFeatures FailedFeatures,
    string? Detail = null)
{
    public WindowFeatureApplyStatus Status => FailedFeatures != WindowFeatures.None
        ? WindowFeatureApplyStatus.Failed
        : UnsupportedFeatures != WindowFeatures.None
            ? WindowFeatureApplyStatus.Unsupported
            : WindowFeatureApplyStatus.Applied;

    public WindowFeatures Features => AppliedFeatures | UnsupportedFeatures | FailedFeatures;

    public bool IsComplete => UnsupportedFeatures == WindowFeatures.None && FailedFeatures == WindowFeatures.None;

    public static WindowFeatureApplyResult Applied(WindowFeatures features) =>
        new(features, WindowFeatures.None, WindowFeatures.None);

    public static WindowFeatureApplyResult Unsupported(WindowFeatures features, string? detail = null) =>
        new(WindowFeatures.None, features, WindowFeatures.None, detail);

    public static WindowFeatureApplyResult Failed(WindowFeatures features, string? detail = null) =>
        new(WindowFeatures.None, WindowFeatures.None, features, detail);

    public static WindowFeatureApplyResult Partial(
        WindowFeatures applied,
        WindowFeatures unsupported,
        WindowFeatures failed,
        string? detail = null) =>
        new(applied, unsupported, failed, detail);
}
