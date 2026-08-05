namespace SecRandom.Mobile;

/// <summary>
/// Supplies native keyboard occlusion when a platform does not expose it through Avalonia's input pane.
/// </summary>
public interface IMobileKeyboardOcclusionSource
{
    event EventHandler<MobileKeyboardOcclusionChangedEventArgs>? Changed;
}

public sealed class MobileKeyboardOcclusionChangedEventArgs(
    double occludedHeight,
    TimeSpan animationDuration) : EventArgs
{
    public double OccludedHeight { get; } = occludedHeight;
    public TimeSpan AnimationDuration { get; } = animationDuration;
}
