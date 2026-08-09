namespace SecRandom.Core.Views;

public sealed class ViewSession
{
    private readonly TaskCompletionSource<ViewCloseResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ViewSession(string viewId, ViewBase view, IViewHost host, ViewPresentation presentation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(host);

        ViewId = viewId;
        View = view;
        Host = host;
        Presentation = presentation;
    }

    public string ViewId { get; }
    public ViewBase View { get; }
    public IViewHost Host { get; }
    public ViewPresentation Presentation { get; }
    public Task<ViewCloseResult> Completion => _completion.Task;

    public bool TryComplete(ViewCloseResult closeResult) => _completion.TrySetResult(closeResult);
}
