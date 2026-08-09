namespace SecRandom.Core.Views;

public sealed class ViewCloseRequest(ViewCloseReason reason, object? result, bool isCancelable)
{
    public ViewCloseReason Reason { get; } = reason;
    public object? Result { get; } = result;
    public bool IsCancelable { get; } = isCancelable;
}
