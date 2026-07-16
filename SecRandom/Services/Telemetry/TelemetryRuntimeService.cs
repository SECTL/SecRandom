using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sentry;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;

namespace SecRandom.Services.Telemetry;

public sealed class TelemetryRuntimeService : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan DefaultFlushTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly ILogger<TelemetryRuntimeService> _logger;
    private readonly MainConfigHandler _mainConfigHandler;
    private PrivacySettingsConfig _privacySettings;
    private readonly ITelemetrySdkAdapter _sdkAdapter;

    private bool _disposed;
    private bool _isInitializing;
    private bool _isInitialized;
    private bool _isShutdown;
    private TelemetryPolicySnapshot _currentPolicy = TelemetryPolicySnapshot.From(false);

    public TelemetryRuntimeService(
        ILogger<TelemetryRuntimeService> logger,
        MainConfigHandler mainConfigHandler,
        ITelemetrySdkAdapter sdkAdapter)
    {
        _logger = logger;
        _mainConfigHandler = mainConfigHandler;
        _privacySettings = mainConfigHandler.Data.General.PrivacySettings;
        _sdkAdapter = sdkAdapter;
        _privacySettings.PropertyChanged += PrivacySettingsOnPropertyChanged;
    }

    public TelemetryPolicySnapshot CurrentPolicy => _currentPolicy;

    public TelemetryPolicySnapshot ResolvePolicy()
    {
        return TelemetryPolicySnapshot.From(_privacySettings.ShouldInitializeSentryTelemetry);
    }

    public async Task ApplyCurrentPolicyAsync(CancellationToken cancellationToken = default)
    {
        TelemetryPolicySnapshot policy = ResolvePolicy();

        if (policy.ShouldInitializeSdk)
        {
            await InitializeAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await DisableAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_privacySettings, _mainConfigHandler.Data.General.PrivacySettings))
                return;

            _privacySettings.PropertyChanged -= PrivacySettingsOnPropertyChanged;
            _privacySettings = _mainConfigHandler.Data.General.PrivacySettings;
            _privacySettings.PropertyChanged += PrivacySettingsOnPropertyChanged;
        }

        await ApplyCurrentPolicyAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        TelemetryPolicySnapshot policy;

        lock (_gate)
        {
            if (_disposed)
            {
                _logger.LogDebug("Skipping telemetry initialization because runtime is already disposed.");
                return;
            }

            if (_isInitializing || _isInitialized)
            {
                _logger.LogDebug("Skipping telemetry initialization because runtime is already initialized or initializing.");
                return;
            }

            _isInitializing = true;
            policy = ResolvePolicy();
            _currentPolicy = policy;
            _isShutdown = false;
        }

        _logger.LogInformation(
            "Sentry telemetry policy resolved. Enabled={IsEnabled}, Upload={ShouldUploadTelemetry}, PII={SendDefaultPii}.",
            policy.IsEnabled,
            policy.ShouldUploadTelemetry,
            policy.SendDefaultPii);

        if (!policy.ShouldInitializeSdk)
        {
            lock (_gate)
            {
                _isInitializing = false;
                _isInitialized = false;
                _isShutdown = true;
            }

            return;
        }

        try
        {
            await _sdkAdapter.InitializeAsync(policy, cancellationToken).ConfigureAwait(false);

            lock (_gate)
            {
                _isInitializing = false;
                _isInitialized = true;
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _isInitializing = false;
                _isInitialized = false;
                _isShutdown = true;
            }

            _logger.LogError(ex, "Telemetry SDK initialization failed. Application startup will continue without telemetry upload.");
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return FlushAsync(DefaultFlushTimeout, cancellationToken);
    }

    public Task CaptureExceptionAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        TelemetryPolicySnapshot policy;

        lock (_gate)
        {
            if (_disposed || !_isInitialized || _isShutdown)
                return Task.CompletedTask;

            policy = _currentPolicy;
        }

        if (!policy.ShouldUploadTelemetry)
            return Task.CompletedTask;

        return _sdkAdapter.CaptureExceptionAsync(exception, DefaultFlushTimeout, cancellationToken);
    }

    /// <summary>
    /// 启动性能追踪事务，仅在遥测已启用且追踪已开启时执行。
    /// </summary>
    public ISpan? StartTransaction(string name, string operation)
    {
        TelemetryPolicySnapshot policy;

        lock (_gate)
        {
            if (_disposed || !_isInitialized || _isShutdown)
                return null;

            policy = _currentPolicy;
        }

        if (!policy.ShouldUploadTelemetry || !policy.EnableTraces)
            return null;

        return SentrySdk.StartTransaction(name, operation);
    }

    public Task FlushAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        TelemetryPolicySnapshot policy;

        lock (_gate)
        {
            if (_disposed || !_isInitialized || _isShutdown)
                return Task.CompletedTask;

            policy = _currentPolicy;
        }

        if (!policy.ShouldUploadTelemetry)
            return Task.CompletedTask;

        return _sdkAdapter.FlushAsync(timeout, cancellationToken);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return ShutdownAsync(DefaultShutdownTimeout, cancellationToken);
    }

    public Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        return ShutdownCoreAsync(timeout, cancellationToken, allowDisposed: false);
    }

    private async Task DisableAsync(CancellationToken cancellationToken)
    {
        await ShutdownAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_disposed)
                return;

            _currentPolicy = TelemetryPolicySnapshot.From(false);
            _isInitializing = false;
            _isInitialized = false;
            _isShutdown = true;
        }
    }

    private async Task ShutdownCoreAsync(TimeSpan timeout, CancellationToken cancellationToken, bool allowDisposed)
    {
        TelemetryPolicySnapshot policy;

        lock (_gate)
        {
            if ((!allowDisposed && _disposed) || !_isInitialized || _isShutdown)
                return;

            _isShutdown = true;
            _isInitialized = false;
            policy = _currentPolicy;
        }

        if (!policy.ShouldInitializeSdk)
            return;

        await _sdkAdapter.ShutdownAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    private void PrivacySettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(PrivacySettingsConfig.SentryTelemetryEnabled))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyCurrentPolicyAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply telemetry privacy setting.");
            }
        });
    }

    public void Dispose()
    {
        DisposeCore(sync: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore(sync: false);
        GC.SuppressFinalize(this);
    }

    private async ValueTask DisposeAsyncCore(bool sync)
    {
        if (!DisposeCore(sync))
            return;

        await ShutdownCoreAsync(DefaultShutdownTimeout, CancellationToken.None, allowDisposed: true).ConfigureAwait(false);

        if (!sync)
            await _sdkAdapter.DisposeAsync().ConfigureAwait(false);
    }

    private bool DisposeCore(bool sync)
    {
        lock (_gate)
        {
            if (_disposed)
                return false;

            _disposed = true;
        }

        _privacySettings.PropertyChanged -= PrivacySettingsOnPropertyChanged;

        if (sync)
            _sdkAdapter.Dispose();

        return true;
    }
}
