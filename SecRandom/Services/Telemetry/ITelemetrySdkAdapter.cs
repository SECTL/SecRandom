using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Telemetry;

public interface ITelemetrySdkAdapter : IDisposable, IAsyncDisposable
{
    bool IsInitialized { get; }

    Task InitializeAsync(TelemetryPolicySnapshot policy, CancellationToken cancellationToken = default);

    Task CaptureExceptionAsync(Exception exception, TimeSpan flushTimeout, CancellationToken cancellationToken = default);

    Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
