using Microsoft.Extensions.Logging;
using SecRandom.Core;
using Sentry;

namespace SecRandom.Services.Telemetry;

public sealed class SentryTelemetrySdkAdapter : ITelemetrySdkAdapter
{
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

    /// <summary>
    /// 启动性能追踪事务，采样率由初始化选项中的 TracesSampleRate 决定。
    /// </summary>
    public ITelemetryTransaction? StartTransaction(string name, string operation)
    {
        if (!IsInitialized)
            return null;

        return new SentryTelemetryTransaction(SentrySdk.StartTransaction(name, operation));
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
        options.Dsn = GlobalConstants.SentryDsn;
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
        ConfigureSensitiveEventScrubber(options);
    }

    internal static void ConfigureFeedbackOptions(SentryOptions options)
    {
        options.Dsn = GlobalConstants.SentryDsn;
        options.Release = GlobalConstants.VersionLong;
        options.Environment = GlobalConstants.IsDevelopment ? "development" : "production";
        options.Debug = true;
        options.AutoSessionTracking = false;
        options.EnableLogs = false;
        options.SendDefaultPii = false;
        options.TracesSampleRate = 0;
        options.ProfilesSampleRate = 0;
        options.TracePropagationTargets.Clear();
        options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        ConfigureSensitiveEventScrubber(options);
    }

    private static void ConfigureSensitiveEventScrubber(SentryOptions options)
    {
        // 清理事件敏感数据：移除服务器名称和用户标识
        options.SetBeforeSend((sentryEvent, hint) =>
        {
            sentryEvent.ServerName = null;
#pragma warning disable CS8625 // SentryUser 属性 setter 支持 null，但属性类型标记为非可空
            sentryEvent.User = null;
#pragma warning restore CS8625
            return sentryEvent;
        });

        // Sentry.Profiling 的 Android/iOS build targets 不受支持；移动构建只保留异常与事务遥测。
        // if (policy.EnableProfiles && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()))
        //     options.AddProfilingIntegration(ProfilingStartupTimeout);
    }

        /// <summary>
        /// 将 Sentry ISpan 适配为 SDK 中立的 <see cref="ITelemetryTransaction"/>。
        /// </summary>
        private sealed class SentryTelemetryTransaction(ISpan span) : ITelemetryTransaction
        {
            public void Finish(TelemetryTransactionStatus status)
            {
                span.Finish(ToSpanStatus(status));
            }

            public void Finish(Exception exception, TelemetryTransactionStatus status)
            {
                span.Finish(exception, ToSpanStatus(status));
            }

            public void Dispose()
            {
                span.Dispose();
            }

            private static SpanStatus ToSpanStatus(TelemetryTransactionStatus status) => status switch
            {
                TelemetryTransactionStatus.Ok => SpanStatus.Ok,
                TelemetryTransactionStatus.PermissionDenied => SpanStatus.PermissionDenied,
                _ => SpanStatus.InternalError
            };
        }
    }
