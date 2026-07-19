namespace SecRandom.Core.Views;

public sealed class ViewShowOptions
{
    public ViewActivationPreference ActivationPreference { get; init; } = ViewActivationPreference.Default;
    public string? HostId { get; init; }
    public ViewPresentation? Presentation { get; init; }
    public bool ReuseExistingView { get; init; } = true;
}
