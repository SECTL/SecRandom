namespace SecRandom.Core.Views;

public interface IViewEngine
{
    Task<IViewHandle> ShowAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default);
    Task<ViewCloseResult> ShowModalAsync(string viewId, ViewShowOptions? options = null, CancellationToken cancellationToken = default);
    Task<ViewCloseResult> CloseAsync(string viewId, ViewCloseReason reason = ViewCloseReason.Programmatic, object? result = null, CancellationToken cancellationToken = default);
    Task CloseHostAsync(IViewHost host, ViewCloseReason reason, CancellationToken cancellationToken = default);
}
