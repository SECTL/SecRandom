namespace SecRandom.Core.Views;

public interface IViewHandle
{
    string ViewId { get; }
    Task<ViewCloseResult> Completion { get; }
    Task<ViewCloseResult> CloseAsync(ViewCloseReason reason = ViewCloseReason.Programmatic, object? result = null, CancellationToken cancellationToken = default);
}
