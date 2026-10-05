using Microsoft.Extensions.Logging;

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
///         <b>重连策略必须区分两类失败</b>：<c>unauthorized</c> 值得刷新凭据后重试；
///         <c>invalid_request</c> / <c>node_not_found</c> / <c>group_not_found</c> 重试一万次
///         也不会成功，只会刷日志并打满服务端——它们进入 <see cref="ControlNodeLinkStatus.Blocked" />
///         并等待用户修正配置（设置页的"重新连接"会重新唤醒）。
///     </para>
///     <para>
///         断线退避是 1s → 2s → 4s → …（上限 60s）加抖动：上百台教室机同时重连会把服务端打满。
///     </para>
/// </remarks>
public sealed class ControlNodeClient
{
    private readonly IControlNodeTransportFactory _transportFactory;
    private readonly IControlNodeCredentialProvider _credentialProvider;
    private readonly IControlNodeStateStore _stateStore;
    private readonly IControlCommandDispatcher _dispatcher;
    private readonly ControlNodeClientOptions _options;
    private readonly ILogger<ControlNodeClient> _logger;
    private readonly ILogger<ControlNodeSession> _sessionLogger;
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    private ControlNodeLinkState _linkState = new(ControlNodeLinkStatus.Disabled);

    /// <summary>当前活跃会话（没有连接时为 <c>null</c>）：注销只能发给一条已经建好的连接。</summary>
    private ControlNodeSession? _activeSession;

    /// <summary>
    ///     自我注销（只在**退出登录**与**换组**时调用）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>未连接时直接返回 false，不为注销去连一次</b>：注销是"把已经存在的登记撤掉"，
    ///         专门建连反而会在退出路径上多花时间，还可能把已经停掉的服务又拉起来。
    ///     </para>
    ///     <para>
    ///         退出程序/关窗口/后台驻留结束/崩溃恢复/更新重启**都不调用**：服务端"登记即列出"，
    ///         离线只显示 <c>online:false</c>；在关闭路径上注销会让教室机一关软件就从控制台消失。
    ///         一句话：**退出登录 = 注销；退出程序 = 不注销，保持 offline 可见**。
    ///     </para>
    /// </remarks>
    public async Task<bool> DeregisterAsync(
        string groupId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var session = Volatile.Read(ref _activeSession);
        if (session is null)
            return false;

        return await session.DeregisterAsync(groupId, timeout, cancellationToken).ConfigureAwait(false);
    }

    public ControlNodeClient(
        IControlNodeTransportFactory transportFactory,
        IControlNodeCredentialProvider credentialProvider,
        IControlNodeStateStore stateStore,
        IControlCommandDispatcher dispatcher,
        ControlNodeClientOptions options,
        ILogger<ControlNodeClient> logger,
        ILogger<ControlNodeSession> sessionLogger)
    {
        _transportFactory = transportFactory;
        _credentialProvider = credentialProvider;
        _stateStore = stateStore;
        _dispatcher = dispatcher;
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
        var consecutiveUnauthorized = 0;

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

                var token = await TryGetTokenAsync(consecutiveUnauthorized > 0, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Idle, "not_signed_in"));
                    await WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 明文 ws:// 只允许回环：Bearer 凭据会经过这条链路，不能明文出网。
                if (!ControlEndpointPolicy.TryValidate(state.ServerUrl, out var endpoint, out var endpointError))
                {
                    _logger.LogWarning("集控节点地址无效（{Error}）：{Endpoint}", endpointError, state.ServerUrl);
                    SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Blocked, endpointError));
                    await WaitAsync(null, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Connecting));

                var result = await RunSingleSessionAsync(state, endpoint!, token, cancellationToken).ConfigureAwait(false);

                if (result.Reason == ControlSessionEndReason.Stopped)
                    break;

                if (result.HandshakeCompleted)
                    backoff = _options.InitialBackoff;

                switch (result.Reason)
                {
                    case ControlSessionEndReason.Terminal:
                        // 终态：未注册 / 不是组成员 / 请求非法。重试没有意义，等用户修正后唤醒。
                        SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Blocked, result.Detail));
                        await WaitAsync(null, cancellationToken).ConfigureAwait(false);
                        continue;

                    case ControlSessionEndReason.Unauthorized:
                        consecutiveUnauthorized++;
                        if (consecutiveUnauthorized >= _options.MaxConsecutiveUnauthorized)
                        {
                            _logger.LogWarning("集控节点凭据连续被拒 {Count} 次，停止重试，等待重新登录。", consecutiveUnauthorized);
                            SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.Blocked, "unauthorized"));
                            await WaitAsync(null, cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        break;

                    default:
                        consecutiveUnauthorized = 0;
                        break;
                }

                var delay = NextDelay(backoff);
                SetLinkState(new ControlNodeLinkState(ControlNodeLinkStatus.WaitingToRetry, result.Detail, delay));

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
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
        ControlNodeState state,
        Uri endpoint,
        string token,
        CancellationToken cancellationToken)
    {
        IControlNodeTransport? transport = null;
        try
        {
            transport = await _transportFactory
                .ConnectAsync(new ControlNodeConnectRequest(endpoint.ToString(), token, state.NodeId, state.GroupId), cancellationToken)
                .ConfigureAwait(false);

            var session = new ControlNodeSession(
                transport, _stateStore, _dispatcher, _options, _sessionLogger);

            // 只有**收到 hello.ack 之后**才算连上：从发起到握手之间，凭据可能被拒、
            // 节点可能未登记、服务端可能不可达。少了这一步，界面上永远看不到"已连接"。
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

    private async Task<string?> TryGetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        try
        {
            return await _credentialProvider.TryGetAccessTokenAsync(forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "读取 SECTL 凭据失败。");
            return null;
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

    private TimeSpan NextDelay(TimeSpan backoff)
    {
        // 抖动：避免上百台设备在同一毫秒重连。
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        return backoff + jitter;
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
