using System;
using System.Threading;
using System.Threading.Tasks;
using ClassIsland.Shared.IPC;
using ClassIsland.Shared.IPC.Abstractions.Services;
using dotnetCampus.Ipc.CompilerServices.GeneratedProxies;
using dotnetCampus.Ipc.Pipes;
using Microsoft.Extensions.Logging;
using SecRandom4Ci.Interface.Services;

namespace SecRandom.Services.Linkage;

public sealed class ClassIslandIpcConnection : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JsonRouteReadyDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly Version MinimumPluginVersion = new(1, 2, 0, 0);

    private readonly ILogger<ClassIslandIpcConnection> _logger;
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly object _stateLock = new();

    private IpcClient? _client;
    private IPublicLessonsService? _lessonsService;
    private ISecRandomService? _notificationService;
    private DateTimeOffset _nextConnectAttempt = DateTimeOffset.MinValue;
    private TimeSpan _currentRetryDelay = MinRetryDelay;
    private bool _isDisposed;
    private volatile int _connectionState; // 0=disconnected, 1=connecting, 2=connected

    public IPublicLessonsService? LessonsService
    {
        get
        {
            lock (_stateLock)
            {
                return _lessonsService;
            }
        }
    }

    public ISecRandomService? NotificationService
    {
        get
        {
            lock (_stateLock)
            {
                return _notificationService;
            }
        }
    }

    public bool IsConnected
    {
        get
        {
            lock (_stateLock)
            {
                return _lessonsService is not null;
            }
        }
    }

    public event EventHandler? StateChanged;

    public ClassIslandIpcConnection(ILogger<ClassIslandIpcConnection> logger)
    {
        _logger = logger;
    }

    public async Task<IPublicLessonsService?> GetLessonsServiceAsync(CancellationToken cancellationToken = default)
    {
        var service = LessonsService;
        if (service is not null)
            return service;

        // Trigger connection if not already trying
        if (Interlocked.CompareExchange(ref _connectionState, 1, 0) == 0)
        {
            _ = Task.Run(() => TryConnectAsync(cancellationToken));
        }

        // Wait for connection with timeout
        var timeout = TimeSpan.FromSeconds(10);
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            service = LessonsService;
            if (service is not null)
                return service;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
        return null;
    }

    public async Task<ISecRandomService?> GetNotificationServiceAsync(CancellationToken cancellationToken = default)
    {
        var service = NotificationService;
        if (service is not null)
            return service;

        // Ensure connection is attempted
        await GetLessonsServiceAsync(cancellationToken).ConfigureAwait(false);
        return NotificationService;
    }

    private async Task TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _connectionState, 0);
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _nextConnectAttempt)
            return false;

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return true;

            if (DateTimeOffset.UtcNow < _nextConnectAttempt)
                return false;

            var client = new IpcClient();
            
            // Subscribe to ClassIsland lifecycle notifications (NOT CurrentTimeStateChanged which fires every second)
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnClassNotifyId, OnClassIslandStateChanged);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnBreakingTimeNotifyId, OnClassIslandStateChanged);
            client.JsonIpcProvider.AddNotifyHandler(IpcRoutedNotifyIds.OnAfterSchoolNotifyId, OnClassIslandStateChanged);

            try
            {
                await client.Connect().WaitAsync(ConnectTimeout, cancellationToken).ConfigureAwait(false);
                await Task.Delay(JsonRouteReadyDelay, cancellationToken).ConfigureAwait(false);

                if (client.PeerProxy is null)
                {
                    DisposeClient(client);
                    ScheduleRetry();
                    return false;
                }

                // Handle connection broken - on the PEER, not the provider
                client.PeerProxy!.PeerConnectionBroken += (_, _) => OnPeerConnectionBroken();

                var lessons = GeneratedIpcFactory.CreateIpcProxy<IPublicLessonsService>(client.Provider, client.PeerProxy);

                // Test if lessons service works (ClassIsland core IPC)
                bool lessonsWork = false;
                try
                {
                    _ = lessons.IsTimerRunning;
                    lessonsWork = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Lessons service test failed: {ex.Message}");
                }

                // Try to get notification service (SecRandom4Ci plugin - optional)
                ISecRandomService? notification = null;
                try
                {
                    notification = client.Provider.CreateIpcProxy<ISecRandomService>(client.PeerProxy);
                    var isAlive = notification.IsAlive();
                    if (!string.Equals(isAlive, "Yes", StringComparison.Ordinal))
                    {
                        notification = null;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Notification service not available: {ex.Message}");
                    notification = null;
                }

                if (!lessonsWork)
                {
                    _logger.LogDebug("ClassIsland IPC 连接成功但课程服务不可用。");
                    DisposeClient(client);
                    ScheduleRetry();
                    return false;
                }

                _client = client;
                _lessonsService = lessons;
                _notificationService = notification;
                _nextConnectAttempt = DateTimeOffset.MinValue;
                _currentRetryDelay = MinRetryDelay;

                if (notification is not null)
                {
                    _logger.LogInformation("已连接到 ClassIsland IPC：管道={PipeName}，SecRandom4Ci 插件可用。", IpcClient.PipeName);
                }
                else
                {
                    _logger.LogInformation("已连接到 ClassIsland IPC：管道={PipeName}，仅课程联动可用（未安装 SecRandom4Ci 插件）。", IpcClient.PipeName);
                }

                // Notify state changed on successful connection
                StateChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "连接 ClassIsland IPC 失败，将在 {RetryDelay} 后重试。", _currentRetryDelay);
                DisposeClient(client);
                ScheduleRetry();
                return false;
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    private void OnPeerConnectionBroken()
    {
        if (_isDisposed)
            return;

        _logger.LogDebug("ClassIsland IPC 连接已断开，将尝试重连。");
        InvalidateConnection();
        ScheduleRetry();
        
        // Delay reconnection to avoid race with ClassIsland's broadcast loop
        // ClassIsland broadcasts currentTimeStateChanged every second; 
        // immediate reconnect can race with its BroadcastNotificationAsync
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            if (!_isDisposed)
                await TryConnectAsync(CancellationToken.None).ConfigureAwait(false);
        });
        
        // Notify state changed on disconnection
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnClassIslandStateChanged()
    {
        if (_isDisposed)
            return;

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void InvalidateConnection()
    {
        lock (_stateLock)
        {
            _lessonsService = null;
            _notificationService = null;
        }
        DisposeClient(_client);
        _client = null;
    }

    private static void DisposeClient(IpcClient? client)
    {
        if (client is null)
            return;

        try
        {
            client.Provider.Dispose();
        }
        catch (Exception)
        {
        }
    }

    private void ScheduleRetry()
    {
        _nextConnectAttempt = DateTimeOffset.UtcNow.Add(_currentRetryDelay);
        _currentRetryDelay = TimeSpan.FromSeconds(Math.Min(_currentRetryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        InvalidateConnection();
        _connectionGate.Dispose();
    }
}