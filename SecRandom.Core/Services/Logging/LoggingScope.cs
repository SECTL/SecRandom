namespace SecRandom.Core.Services.Logging;

public class LoggingScope(Action onDispose) : IDisposable
{
    public void Dispose()
    {
        onDispose?.Invoke();
    }
}