namespace SecRandom.Core.Views;

public enum ViewPresentation
{
    Page,

    /// <summary>
    /// Presents the view as a modal overlay without an automatic navigation bar.
    /// </summary>
    /// <remarks>
    /// Callers must provide any close or back behavior and explicitly handle the view's close result when needed.
    /// </remarks>
    Modal
}
