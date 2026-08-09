namespace SecRandom.Core.Views;

public sealed class ViewClosingEventArgs(ViewCloseReason reason, object? result, bool isCancelable) : EventArgs
{
    public ViewCloseReason Reason { get; } = reason;
    public object? Result { get; set; } = result;
    public bool IsCancelable { get; } = isCancelable;
    public bool Cancel { get; set; }
}
