namespace SecRandom.Core.Views;

public sealed class ViewCloseResult
{
    private ViewCloseResult(ViewCloseReason reason, object? result, bool wasClosed, bool wasCanceled, string? errorMessage)
    {
        Reason = reason;
        Result = result;
        WasClosed = wasClosed;
        WasCanceled = wasCanceled;
        ErrorMessage = errorMessage;
    }

    public ViewCloseReason Reason { get; }
    public object? Result { get; }
    public bool WasClosed { get; }
    public bool WasCanceled { get; }
    public string? ErrorMessage { get; }

    public static ViewCloseResult Success(ViewCloseReason reason, object? result = null) =>
        new(reason, result, true, false, null);

    public static ViewCloseResult Canceled(ViewCloseReason reason, object? result = null) =>
        new(reason, result, false, true, null);

    public static ViewCloseResult Failed(ViewCloseReason reason, object? result, string errorMessage) =>
        new(reason, result, false, false, errorMessage);

    public static ViewCloseResult AlreadyClosed(ViewCloseReason reason, object? result = null) =>
        new(reason, result, true, false, null);
}
