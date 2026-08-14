using System;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Services.Plugins;

/// <summary>
///     App-layer implementation of <see cref="IAppLifecycleService"/> that forwards the host's
///     <see cref="App.AppStarted"/> / <see cref="App.AppStopping"/> events.
/// </summary>
public sealed class AppLifecycleService : IAppLifecycleService, IDisposable
{
    private readonly App _app;
    private bool _disposed;

    public AppLifecycleService()
    {
        _app = App.Current;
        _app.AppStarted += OnAppStarted;
        _app.AppStopping += OnAppStopping;
    }

    public event EventHandler? AppStarted;

    public event EventHandler? AppStopping;

    private void OnAppStarted(object? sender, EventArgs e)
    {
        AppStarted?.Invoke(this, e);
    }

    private void OnAppStopping(object? sender, EventArgs e)
    {
        AppStopping?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _app.AppStarted -= OnAppStarted;
        _app.AppStopping -= OnAppStopping;
    }
}
