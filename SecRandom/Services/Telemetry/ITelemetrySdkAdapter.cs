using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecRandom.Services.Telemetry;

public interface ITelemetrySdkAdapter : IDisposable, IAsyncDisposable
{
    bool IsInitialized { get; }

    Task InitializeAsync(TelemetryPolicySnapshot policy, CancellationToken cancellationToken = default);

    Task CaptureExceptionAsync(Exception exception, TimeSpan flushTimeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// 启动性能追踪事务；SDK 未初始化时返回 null。采样策略由 SDK 初始化选项决定。
    /// </summary>
    ITelemetryTransaction? StartTransaction(string name, string operation);

    Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
