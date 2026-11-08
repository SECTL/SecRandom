using Microsoft.Extensions.Logging;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     集控节点客户端：维持一条出站长连接，并在断开后按退避重连。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么必须是出站长连接</b>：教室机在 NAT 之后，控制台永远连不到它。
///         所有远程控制都是"控制台 → 服务端 → 顺着节点自己的连接推下去"。
///     </para>
///     <para>
///         <b>v4 的凭据来自接入记录，不再来自 SECTL 账号</b>：<c>device_id</c> + <c>secret</c>
///         只在本机加密保存，握手时放在第一帧 <c>hello</c> 的载荷里。
///     </para>
///     <para>
///         <b>重连必须区分两类失败</b>：网络抖动值得退避重试；<c>bad_secret</c>/<c>device_revoked</c>
///         说明凭据已经作废，<c>protocol_too_old</c> 说明客户端版本太旧——重试一万次也不会成功，
///         只会刷日志并打满服务端，因此进入 <see cref="ControlNodeLinkStatus.Blocked" />，
///         等用户重新接入或升级。凭据失效时同时清掉本地记录，避免下次启动又拿同一个坏凭据去撞。
///     </para>
///     <para>
///         断线退避是 1s → 2s → 4s → …（上限 60s）加 ±20% 抖动：上百台教室机同时重连会把服务端打满。
///     </para>
/// </remarks>
public sealed class ControlNodeClient
{
    private readonly IControlNodeTransportFactory _transportFactory;
    private readonly INodeEnrollmentStore? _enrollmentStore;
    private readonly IControlNodeStateStore _stateStore;
    private readonly IControlCommandDispatcher _dispatcher;
    private readonly IControlSettingsReportSource? _settingsReportSource;
    private readonly ControlNodeClientOptions _options;
    private readonly ILogger<ControlNodeClient> _logger;
    private readonly ILogger<ControlNodeSession> _sessionLogger;
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    private ControlNodeLinkState _linkState = new(ControlNodeLinkStatus.Disabled);

    /// <summary>当前活跃会话（没有连接时为 <c>null</c>）。</summary>
    private ControlNodeSession? _activeSession;

    /// <summary>
    ///     v4 没有"注销节点"帧：离线只体现在心跳消失，服务端按设备记录保持可见。
    ///     保留这个方法是为了不破坏既有调用点（退出登录路径），但它是空操作。
    /// </summary>
    public async Task<bool> DeregisterAsync(
        string groupId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        return false;
    }

    public ControlNodeClient(
        IControlNodeTransportFactory transportFactory,
        IControlNodeCredentialProvider credentialProvider,
        IControlNodeStateStore stateStore,
        IControlCommandDispatcher dispatcher,
        ControlNodeClientOptions options,
        ILogger<ControlNodeClient> logger,
        ILogger<ControlNodeSession> sessionLogger,
        INodeEnrollmentStore? enrollmentStore = null,
        IControlSettingsReportSource? settingsReportSource = null)
    {
        _transportFactory = transportFactory;
        _enrollmentStore = enrollmentStore;
        _stateStore = stateStore;
        _dispatcher = dispatcher;
        _settingsReportSource = settingsReportSource;
        _options = options;
        _logger = logger;
        _sessionLogger = sessionLogger;
    }

    public ControlNodeLinkState LinkState => _linkState;

    public event EventHandler<ControlNodeLinkState>? LinkStateChanged;

    /// <summary>唤醒等待中的客户端（本机开关、配置变化，或用户点了"重新连接"）。</summary>
    public void Wake()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已经有一个待处理的唤醒信号。
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        EventHandler<ControlNodeState> onStateChanged = (_, _) => Wake();
        _stateStore.Changed += onStateChanged;

        var backoff = _options.InitialBackoff;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var state = _stateStore.Current;

                // 本机开关关闭：不连接。这是设备自己的权威，服务端怎么下发都不会生效。
                if (!state.RemoteControlEnabled)
                {
                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Disabled));
                    await WaitAsync(TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!state.IsConfigured)
                {
                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Idle, "not_configured"));
                    await WaitAsync(TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!TryGetCredential(out var credential))
                {
                    SetLinkState(new ControlNodeLinkState(
                        ControlNodeLinkStatus.Idle,
                        _enrollmentStore?.Status.IsUnreadable == true
                            ? "enrollment_unreadable"
                            : ControlNodeCredentialDetailCodes.NotSignedIn));
                    await WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 明文 ws:// 只允许回环：设备凭据会经过这条链路，不能明文出网。
                if (!ControlEndpointPolicy.TryValidate(state.ServerUrl, out var endpoint, out var endpointError))
                {
                    _logger.LogWarning("集控节点地址无效（{Error}）：{Endpoint}", endpointError, state.ServerUrl);
                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Blocked, endpointError));
                    await WaitAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Connecting));

                var result = await RunSingleSessionAsync(endpoint!, credential, cancellationToken).ConfigureAwait(false);

                if (result.Reason == ControlSessionEndReason.Stopped)
                    break;

                if (result.HandshakeCompleted)
                    backoff = _options.InitialBackoff;

                if (result.Reason == ControlSessionEndReason.Terminal)
                {
                    if (result.Detail is ControlV4Protocol.ErrorCodes.BadSecret or ControlV4Protocol.ErrorCodes.DeviceRevoked)
                    {
                        // 坏凭据不能留在磁盘上：留着只会下次启动继续撞。
                        _logger.LogWarning("集控设备凭据已失效（{Code}），清空本机接入记录。", result.Detail);
                        _enrollmentStore?.Clear();
                        _logger.LogInformation("已清空接入记录，等待用户重新接入。");
                    }

                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Blocked, result.Detail));
                    await WaitAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var delay = NextDelay(backoff);
                SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.WaitingToRetry, result.Detail, delay));

                // 退避等待必须可唤醒：用户翻开关、改地址、点"重新连接"都不该干等最长 60 秒。
                // 但**先把残留信号清掉**——连接过程中到达的那次唤醒与本轮退避无关，
                // 留着它会让退避立刻返回，把 1s→2s→4s 的节奏打乱成"看着很勤快"的重连风暴。
                DrainWakeSignal();
                await WaitAsync(delay, cancellationToken).ConfigureAwait(false);
                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _options.MaxBackoff.Ticks));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 进程退出或服务停止：正常路径。
        }
        finally
        {
            _stateStore.Changed -= onStateChanged;
            SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Disabled, "stopped"));
        }
    }

    private async Task<ControlSessionResult> RunSingleSessionAsync(
        Uri endpoint,
        NodeDeviceCredential credential,
        CancellationToken cancellationToken)
    {
        IControlNodeTransport? transport = null;
        try
        {
            transport = await _transportFactory
                .ConnectAsync(new ControlNodeConnectRequest(endpoint.ToString()), cancellationToken)
                .ConfigureAwait(false);

            // 停止时主动掐断这条连接：只把取消令牌置位的话，卡在握手或接收上的会话要等到底层
            // 超时才结束（WebSocket 最长几十秒），而重启路径等不起——同一 device 同时存在两条循环
            // 会被服务端互相取代。取消回调与下面的 finally 会各 Dispose 一次，因此
            // DisposeAsync 必须可重复调用（WebSocket 实现本来就是幂等的），这是有意为之。
            using var abortOnCancel = cancellationToken.Register(
                static state => _ = ((IControlNodeTransport)state!).DisposeAsync(),
                transport);

            var session = new ControlNodeSession(
                transport, _stateStore, _dispatcher, credential, _options, _sessionLogger,
                settingsReportSource: _settingsReportSource);

            // 只有**收到 hello 的 res.ok:true 之后**才算连上。
            void OnHandshaken(object? sender, int heartbeatSeconds) =>
                SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Connected));

            session.Handshaken += OnHandshaken;
            Volatile.Write(ref _activeSession, session);
            try
            {
                return await session.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                session.Handshaken -= OnHandshaken;
                Volatile.Write(ref _activeSession, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlSessionResult(ControlSessionEndReason.Stopped);
        }
        catch (ControlNodeConnectRejectedException exception)
        {
            // 连接**建立阶段**就被拒（HTTP 401/403）：服务端已经明确说凭据不行，不要当成断网。
            _logger.LogWarning("集控节点连接被服务端拒绝（HTTP {StatusCode}），凭据可能已失效。",
                (int)(exception.StatusCode ?? 0));
            return exception.IsTerminal
                ? new ControlSessionResult(ControlSessionEndReason.Terminal, ControlV4Protocol.ErrorCodes.BadSecret)
                : new ControlSessionResult(ControlSessionEndReason.ConnectionLost, exception.Code);
        }
        catch (Exception exception)
        {
            // 连接建立失败（DNS、代理、证书、服务端未启动）都退避重试，不当成终态。
            _logger.LogWarning(exception, "集控节点连接失败。");
            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, exception.GetType().Name);
        }
        finally
        {
            if (transport is not null)
                await transport.DisposeAsync().ConfigureAwait(false);
        }
    }

    private bool TryGetCredential(out NodeDeviceCredential credential)
    {
        credential = default!;
        if (_enrollmentStore is null || !_enrollmentStore.TryGetCredential(out var stored) || stored is null)
            return false;

        credential = stored;
        return !string.IsNullOrWhiteSpace(credential.DeviceId) && !string.IsNullOrWhiteSpace(credential.Secret);
    }

    /// <summary>
    ///     清掉已经积压的唤醒信号（信号量上限是 1，循环只为把语义写清楚）。
    /// </summary>
    private void DrainWakeSignal()
    {
        while (_wakeSignal.Wait(0))
        {
            // 丢弃与本轮退避无关的旧唤醒。
        }
    }

    /// <summary>
    ///     等待下一次唤醒。<paramref name="timeout" /> 为 <c>null</c> 时一直等到被唤醒或进程结束。
    /// </summary>
    private async Task WaitAsync(TimeSpan? timeout, CancellationToken cancellationToken)
    {
        try
        {
            if (timeout is { } limit)
                await _wakeSignal.WaitAsync(limit, cancellationToken).ConfigureAwait(false);
            else
                await _wakeSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 调用方检查取消状态。
        }
    }

    /// <summary>退避加 ±20% 抖动，避免上百台设备在同一毫秒重连。</summary>
    private TimeSpan NextDelay(TimeSpan backoff)
    {
        var ratio = _options.BackoffJitterRatio;
        var factor = 1d + (Random.Shared.NextDouble() * 2d - 1d) * ratio;
        return TimeSpan.FromMilliseconds(Math.Max(1d, backoff.TotalMilliseconds * factor));
    }

    private void SetLinkState(ControlNodeLinkState state)
    {
        _linkState = state;

        try
        {
            LinkStateChanged?.Invoke(this, state);
        }
        catch (Exception exception)
        {
            // 订阅方（UI）抛异常不能带走连接循环。
            _logger.LogDebug(exception, "集控连接状态订阅方抛出异常。");
        }
    }
}