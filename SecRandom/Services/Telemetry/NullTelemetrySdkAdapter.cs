using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Telemetry;

public sealed class NullTelemetrySdkAdapter : ITelemetrySdkAdapter
{
    private readonly object _gate = new();
    private bool _disposed;

    public bool IsInitialized { get; private set; }

    public Task InitializeAsync(TelemetryPolicySnapshot policy, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed)
                return Task.CompletedTask;

            IsInitialized = policy.ShouldInitializeSdk;
        }

        return Task.CompletedTask;
    }

    public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task CaptureExceptionAsync(
        Exception exception,
        TimeSpan flushTimeout,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IsInitialized = false;
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            IsInitialized = false;
        }

        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
