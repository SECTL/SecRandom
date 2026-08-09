namespace SecRandom.Core.Views;

public sealed class ViewClosedEventArgs(ViewCloseResult closeResult) : EventArgs
{
    public ViewCloseResult CloseResult { get; } = closeResult;
}
