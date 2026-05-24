using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using Sentry;

namespace SecRandom.Services.Telemetry;

public sealed class SentryTelemetrySdkAdapter : ITelemetrySdkAdapter
{
    private const string Dsn = "https://94f3a9ea4d85aac3b03f6575c23befe9@o4510289605296128.ingest.de.sentry.io/4511439509454928";
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

    private static void ConfigureOptions(SentryOptions options, TelemetryPolicySnapshot policy)
    {
        options.Dsn = Dsn;
        options.Release = GlobalConstants.VersionLong;
        options.Environment = GlobalConstants.IsDevelopment ? "development" : "production";
        options.Debug = GlobalConstants.IsDevelopment;
        options.IsGlobalModeEnabled = true;
        options.AutoSessionTracking = true;
        options.SendDefaultPii = policy.SendDefaultPii;
        options.EnableLogs = policy.EnableLogs;
        options.TracesSampleRate = policy.TracesSampleRate;
        options.ProfilesSampleRate = policy.ProfilesSampleRate;
        options.ShutdownTimeout = TimeSpan.FromSeconds(5);

        if (policy.EnableProfiles)
            options.AddProfilingIntegration(ProfilingStartupTimeout);
    }
}
