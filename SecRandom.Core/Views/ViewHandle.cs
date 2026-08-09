namespace SecRandom.Core.Views;

internal sealed class ViewHandle(ViewSession session, Func<ViewSession, ViewCloseRequest, CancellationToken, Task<ViewCloseResult>> closeAsync) : IViewHandle
{
    public string ViewId => session.ViewId;
    public Task<ViewCloseResult> Completion => session.Completion;

    public Task<ViewCloseResult> CloseAsync(
        ViewCloseReason reason = ViewCloseReason.Programmatic,
        object? result = null,
        CancellationToken cancellationToken = default) =>
        closeAsync(session, new ViewCloseRequest(reason, result, true), cancellationToken);
}
