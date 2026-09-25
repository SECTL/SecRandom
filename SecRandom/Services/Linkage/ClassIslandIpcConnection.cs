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

/// <summary>
/// ClassIsland IPC 的唯一连接入口。课程联动与 SecRandom4Ci 通知共用同一条 <see cref="IpcClient"/>，
/// 避免两个服务各建一条管道并各自订阅广播（issue #274 的 CPU 自激来源之一）。
/// </summary>
public sealed class ClassIslandIpcConnection : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JsonRouteReadyDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan IpcCallTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LessonsWaitTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan NotificationWaitTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromMilliseconds(500);
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
    private volatile int _connectionState; // 0=disconnected, 1=connecting

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

    public Task<IPublicLessonsService?> GetLessonsServiceAsync(CancellationToken cancellationToken = default)
        => WaitForLessonsServiceAsync(LessonsWaitTimeout, cancellationToken);

    /// <summary>
    /// 通知发送路径只等一个很短的窗口：拿不到就立刻走内置回退。等待时间过长会把内置通知
    /// （以及抽取前就该打开的 QuickDraw 结果窗口）拖到十秒之后。
    /// </summary>
    public async Task<ISecRandomService?> GetNotificationServiceAsync(CancellationToken cancellationToken = default)
    {
        var service = NotificationService;
        if (service is not null)
            return service;

        await WaitForLessonsServiceAsync(NotificationWaitTimeout, cancellationToken).ConfigureAwait(false);
        return NotificationService;
    }

    private async Task<IPublicLessonsService?> WaitForLessonsServiceAsync(
        TimeSpan waitTimeout,
        CancellationToken cancellationToken)
    {
        var service = LessonsService;
        if (service is not null)
            return service;

        // 退避期内直接返回：否则每次调用都要白等一整个超时窗口
        if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
            return null;

        if (Interlocked.CompareExchange(ref _connectionState, 1, 0) == 0 && !_isDisposed)
            _ = Task.Run(() => TryConnectAsync());

        var deadline = DateTimeOffset.UtcNow + waitTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            service = LessonsService;
            if (service is not null)
                return service;
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private DateTimeOffset NextConnectAttempt
    {
        get
        {
            lock (_stateLock)
            {
                return _nextConnectAttempt;
            }
        }
        set
        {
            lock (_stateLock)
            {
                _nextConnectAttempt = value;
            }
        }
    }

    private async Task TryConnectAsync()
    {
        try
        {
            await EnsureConnectedAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "连接 ClassIsland IPC 时发生未处理异常。");
        }
        finally
        {
            Interlocked.Exchange(ref _connectionState, 0);
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
            return false;

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsConnected)
                return true;

            if (_isDisposed || DateTimeOffset.UtcNow < NextConnectAttempt)
                return false;

            var client = new IpcClient();

            // 只订阅生命周期事件。ClassIsland 只在状态变化时广播 CurrentTimeStateChanged，
            // 每秒变化的是它自己的主计时器；倒计时由刷新时按需读取，不需要额外订阅。
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

                // 探测课程服务可用性；属性读取也带超时，避免 ClassIsland 卡住时连接流程被拖死
                var lessonsProbe = await TryInvokeWithTimeoutAsync(() => lessons.IsTimerRunning, IpcCallTimeout)
                    .ConfigureAwait(false);
                var lessonsWork = lessonsProbe.Success;

                // SecRandom4Ci 插件为可选依赖，但必须通过版本门槛（旧插件不满足通知契约）
                ISecRandomService? notification = null;
                try
                {
                    notification = client.Provider.CreateIpcProxy<ISecRandomService>(client.PeerProxy);
                    var aliveProbe = await TryInvokeWithTimeoutAsync(notification.IsAlive, IpcCallTimeout)
                        .ConfigureAwait(false);
                    var versionProbe = await TryInvokeWithTimeoutAsync(notification.GetPluginVersion, IpcCallTimeout)
                        .ConfigureAwait(false);
                    var isAlive = aliveProbe.Success ? aliveProbe.Value : null;
                    var pluginVersion = versionProbe.Success ? versionProbe.Value : null;
                    if (!IsNotificationServiceUsable(isAlive, pluginVersion))
                    {
                        _logger.LogDebug(
                            "SecRandom4Ci 插件不可用或版本低于 {MinimumPluginVersion}：IsAlive={IsAlive}，版本={PluginVersion}。",
                            MinimumPluginVersion, isAlive, pluginVersion);
                        notification = null;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "获取 SecRandom4Ci 通知服务失败。");
                    notification = null;
                }

                if (!lessonsWork)
                {
                    _logger.LogDebug("ClassIsland IPC 连接成功但课程服务不可用。");
                    DisposeClient(client);
                    ScheduleRetry();
                    return false;
                }

                lock (_stateLock)
                {
                    _client = client;
                    _lessonsService = lessons;
                    _notificationService = notification;
                }
                NextConnectAttempt = DateTimeOffset.MinValue;
                _currentRetryDelay = MinRetryDelay;

                if (notification is not null)
                {
                    _logger.LogInformation("已连接到 ClassIsland IPC：管道={PipeName}，SecRandom4Ci 插件可用。", IpcClient.PipeName);
                }
                else
                {
                    _logger.LogInformation("已连接到 ClassIsland IPC：管道={PipeName}，仅课程联动可用（未安装 SecRandom4Ci 插件）。", IpcClient.PipeName);
                }

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

    /// <summary>
    /// 通知服务是否可用：插件必须自报存活，并且版本不低于通知契约的最低要求。
    /// </summary>
    internal static bool IsNotificationServiceUsable(string? isAlive, Version? pluginVersion)
        => string.Equals(isAlive, "Yes", StringComparison.Ordinal)
           && pluginVersion is not null
           && pluginVersion >= MinimumPluginVersion;

    /// <summary>
    /// 同步 IPC 调用不能无限等待：ClassIsland 卡住时按超时返回失败，让调用方走“不可用”分支。
    /// </summary>
    private static async Task<(bool Success, T Value)> TryInvokeWithTimeoutAsync<T>(Func<T> invoke, TimeSpan timeout)
    {
        try
        {
            return (true, await Task.Run(invoke).WaitAsync(timeout).ConfigureAwait(false));
        }
        catch (Exception)
        {
            return (false, default!);
        }
    }

    private void OnPeerConnectionBroken()
    {
        if (_isDisposed)
            return;

        _logger.LogDebug("ClassIsland IPC 连接已断开，将尝试重连。");
        InvalidateConnection();
        // 断开后允许立即重连，但留一点间隔，避免与 ClassIsland 的广播/管道清理抢时序
        NextConnectAttempt = DateTimeOffset.MinValue;
        _currentRetryDelay = MinRetryDelay;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(ReconnectDelay).ConfigureAwait(false);
                if (!_isDisposed)
                    await TryConnectAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "ClassIsland IPC 重连失败，等待下一次刷新重试。");
            }
        });

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
        IpcClient? client;
        lock (_stateLock)
        {
            client = _client;
            _client = null;
            _lessonsService = null;
            _notificationService = null;
        }
        DisposeClient(client);
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
        NextConnectAttempt = DateTimeOffset.UtcNow.Add(_currentRetryDelay);
        _currentRetryDelay = TimeSpan.FromSeconds(Math.Min(_currentRetryDelay.TotalSeconds * 2, MaxRetryDelay.TotalSeconds));
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        InvalidateConnection();
        // 不释放 _connectionGate：进行中的等待/连接可能仍持有它
    }
}
