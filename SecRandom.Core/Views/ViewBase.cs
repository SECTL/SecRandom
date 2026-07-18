using Avalonia.Controls;

namespace SecRandom.Core.Views;

public abstract class ViewBase : UserControl
{
    private Func<ViewCloseRequest, CancellationToken, Task<ViewCloseResult>>? _closeHandler;

    public string? ViewId { get; private set; }
    public bool IsActive => _closeHandler is not null && !IsClosed;
    public bool IsClosed { get; private set; }

    public event EventHandler<ViewClosingEventArgs>? Closing;
    public event EventHandler<ViewClosedEventArgs>? Closed;

    public Task<ViewCloseResult> CloseAsync(
        object? result = null,
        ViewCloseReason reason = ViewCloseReason.Programmatic,
        CancellationToken cancellationToken = default)
    {
        var handler = _closeHandler;
        if (handler is null)
            return Task.FromResult(ViewCloseResult.Failed(reason, result, "The view is not attached to the view engine."));

        return handler(new ViewCloseRequest(reason, result, true), cancellationToken);
    }

    internal void Attach(string viewId, Func<ViewCloseRequest, CancellationToken, Task<ViewCloseResult>> closeHandler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(viewId);
        ArgumentNullException.ThrowIfNull(closeHandler);

        if (_closeHandler is not null)
            throw new InvalidOperationException("The view is already attached to the view engine.");

        if (IsClosed)
            throw new InvalidOperationException("A closed view cannot be attached again.");

        ViewId = viewId;
        _closeHandler = closeHandler;
    }

    internal void Detach()
    {
        _closeHandler = null;
    }

    internal bool TryBeginClose(ViewCloseReason reason, object? result, bool isCancelable, out ViewCloseResult closeResult)
    {
        if (IsClosed)
        {
            closeResult = ViewCloseResult.AlreadyClosed(reason, result);
            return false;
        }

        var args = new ViewClosingEventArgs(reason, result, isCancelable);
        Closing?.Invoke(this, args);
        if (args.Cancel && isCancelable)
        {
            closeResult = ViewCloseResult.Canceled(reason, args.Result ?? result);
            return false;
        }

        closeResult = ViewCloseResult.Success(reason, args.Result ?? result);
        return true;
    }

    internal void CompleteClose(ViewCloseResult closeResult)
    {
        if (IsClosed)
            return;

        IsClosed = true;
        Detach();
        Closed?.Invoke(this, new ViewClosedEventArgs(closeResult));
    }
}
