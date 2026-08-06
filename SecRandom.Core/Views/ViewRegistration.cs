namespace SecRandom.Core.Views;

public sealed class ViewRegistration
{
    public required string Id { get; init; }
    public required Type ViewType { get; init; }
    public ViewPresentation DefaultPresentation { get; init; } = ViewPresentation.Page;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("View id is required.", nameof(Id));

        if (!typeof(ViewBase).IsAssignableFrom(ViewType))
            throw new ArgumentException("View type must inherit SecRandom.Core.Views.ViewBase.", nameof(ViewType));

    }
}
