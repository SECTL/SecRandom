namespace SecRandom.Core.Views;

public sealed class ViewHostSelection
{
    private ViewHostSelection(bool isSuccess, bool isUnsupported, IViewHost? host, string? errorMessage)
    {
        IsSuccess = isSuccess;
        IsUnsupported = isUnsupported;
        Host = host;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }
    public bool IsUnsupported { get; }
    public IViewHost? Host { get; }
    public string? ErrorMessage { get; }

    public static ViewHostSelection Success(IViewHost host) => new(true, false, host, null);
    public static ViewHostSelection Unsupported(string message) => new(false, true, null, message);
    public static ViewHostSelection Failed(string message) => new(false, false, null, message);
}
