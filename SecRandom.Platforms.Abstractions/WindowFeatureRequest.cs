namespace SecRandom.Platforms.Abstractions;

[Flags]
public enum WindowFeatures
{
    None = 0,
    Topmost = 1 << 0,
    ToolWindow = 1 << 1,
    SkipTaskSwitcher = 1 << 2,
    NoActivate = 1 << 3,
    ClickThrough = 1 << 4,
    ExcludeFromCapture = 1 << 5
}

public readonly record struct WindowFeatureRequest(WindowFeatures Features, bool Enabled);
