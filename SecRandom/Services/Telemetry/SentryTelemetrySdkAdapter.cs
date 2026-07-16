using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using Sentry;

namespace SecRandom.Services.Telemetry;

public sealed class SentryTelemetrySdkAdapter : ITelemetrySdkAdapter
{
    private const string Dsn = "https://7614b2b2fd46a451e7cb3ed670279e75@o4510689230192640.ingest.us.sentry.io/4511675887910912";
    private static readonly TimeSpan ProfilingStartupTimeout = TimeSpan.FromMilliseconds(500);

    private readonly object _gate = new();
    private readonly ILogger<SentryTelemetrySdkAdapter> _logger;

    private IDisposable? _sentry;
    private bool _disposed;

    public SentryTelemetrySdkAdapter(ILogger<SentryTelemetrySdkAdapter> logger)
    {
        _logger = logger;
    }

    public bool IsInitialized { get; private set; }

    public Task InitializeAsync(TelemetryPolicySnapshot policy, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_disposed || IsInitialized || !policy.ShouldInitializeSdk)
                return Task.CompletedTask;

            _sentry = SentrySdk.Init(options => ConfigureOptions(options, policy));
            IsInitialized = true;
        }

        _logger.LogInformation("Sentry telemetry SDK initialized.");
        return Task.CompletedTask;
    }

    public async Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
            return;

        await SentrySdk.FlushAsync(timeout).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CaptureExceptionAsync(
        Exception exception,
        TimeSpan flushTimeout,
        CancellationToken cancellationToken = default)
    {
        if (!IsInitialized)
            return;

        SentrySdk.CaptureException(exception);
        await SentrySdk.FlushAsync(flushTimeout).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        IDisposable? sentry;

        lock (_gate)
        {
            if (!IsInitialized)
                return;

            sentry = _sentry;
            _sentry = null;
            IsInitialized = false;
        }

        try
        {
            await SentrySdk.FlushAsync(timeout).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            sentry?.Dispose();
        }
    }

    public void Dispose()
    {
        IDisposable? sentry;

        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            sentry = _sentry;
            _sentry = null;
            IsInitialized = false;
        }

        sentry?.Dispose();
        GC.SuppressFinalize(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 配置 Sentry SDK 选项。
    /// </summary>
    private static void ConfigureOptions(SentryOptions options, TelemetryPolicySnapshot policy)
    {
        options.Dsn = Dsn;
        options.Release = GlobalConstants.VersionLong;
        options.Environment = GlobalConstants.IsDevelopment ? "development" : "production";
        options.Debug = true;

        // 桌面应用启用全局模式，跨线程共享同一作用域
        options.IsGlobalModeEnabled = true;
        // 启用会话追踪以支持 Release Health
        options.AutoSessionTracking = true;
        // 禁用 Sentry 结构化日志，保留异常与事务遥测
        options.EnableLogs = false;

        // 桌面客户端不向第三方 HTTP 服务传播 Sentry trace headers
        options.TracePropagationTargets.Clear();

        // 根据隐私策略决定是否发送 PII 数据
        options.SendDefaultPii = policy.SendDefaultPii;

        options.TracesSampleRate = policy.TracesSampleRate;
        options.ProfilesSampleRate = policy.ProfilesSampleRate;

        // SDK 关闭超时，确保事件在应用退出前发送
        options.ShutdownTimeout = TimeSpan.FromSeconds(5);

        // 清理事件敏感数据：移除服务器名称和用户标识
        options.SetBeforeSend((sentryEvent, hint) =>
        {
            sentryEvent.ServerName = null;
#pragma warning disable CS8625 // SentryUser 属性 setter 支持 null，但属性类型标记为非可空
            sentryEvent.User = null;
#pragma warning restore CS8625
            return sentryEvent;
        });

        // 加载性能分析集成（需 Sentry.Profiling 包）
        if (policy.EnableProfiles)
            options.AddProfilingIntegration(ProfilingStartupTimeout);
    }
}
