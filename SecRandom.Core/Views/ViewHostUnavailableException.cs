namespace SecRandom.Core.Views;

public sealed class ViewHostUnavailableException : InvalidOperationException
{
    public ViewHostUnavailableException(ViewHostSelection selection, string viewId)
        : base(selection.ErrorMessage ?? $"No host is available for view '{viewId}'.")
    {
        Selection = selection;
        ViewId = viewId;
    }

    public ViewHostSelection Selection { get; }
    public string ViewId { get; }
}
