using Microsoft.Extensions.Logging;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     一条已建立的节点连接上的协议状态机。
/// </summary>
/// <remarks>
///     <para>
///         这一层不认识 WebSocket：它只依赖 <see cref="IControlNodeTransport" />，
///         因此握手、心跳、命令校验顺序、幂等与 <c>desired_state</c> 收敛都能在测试里
///         用一个内存传输完整驱动。
///     </para>
///     <para>
///         <b>三种东西不能混为一谈</b>：期望状态（"应该是什么样"，不过期、靠 revision 收敛）、
///         动作命令（"做一次"，必须过期、过期即丢弃）与查询（REST，不在这里）。
///         用队列模拟状态会重放，用状态模拟动作会永远触发。
///     </para>
/// </remarks>
public sealed class ControlNodeSession
{
    private readonly IControlNodeTransport _transport;
    private readonly IControlNodeStateStore _stateStore;
    private readonly IControlCommandDispatcher _dispatcher;

    private readonly ControlNodeClientOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ControlCommandLedger _ledger;

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly SemaphoreSlim _stateChangedSignal = new(0, 1);
    private readonly Queue<DateTimeOffset> _recentCommands = new();

    /// <summary>本机限流窗口与上限：窗口内超过这个条数的命令会被拒绝。</summary>
    private const int MaxCommandsPerWindow = 20;

    private static readonly TimeSpan CommandRateWindow = TimeSpan.FromSeconds(10);

    public ControlNodeSession(
        IControlNodeTransport transport,
        IControlNodeStateStore stateStore,
        IControlCommandDispatcher dispatcher,

        ControlNodeClientOptions options,
        ILogger<ControlNodeSession> logger,
        TimeProvider? timeProvider = null)
    {
        _transport = transport;
        _stateStore = stateStore;
        _dispatcher = dispatcher;

        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        // 有界 + 过期：命令 ID 不会长期复用，255 条足以覆盖一次重连补投的窗口。
        _ledger = new ControlCommandLedger(capacity: 512, timeToLive: TimeSpan.FromMinutes(30), _timeProvider);
    }

    /// <summary>服务端下发的心跳间隔；握手前为 <c>null</c>。</summary>
    public int? NegotiatedHeartbeatSeconds { get; private set; }

    /// <summary>
    ///     握手成功（收到 <c>hello.ack</c>）时触发，参数是服务端下发的心跳间隔（秒）。
    /// </summary>
    /// <remarks>
    ///     连接状态必须由这条事件驱动，而不是"发起连接时就算连上了"：
    ///     从发起连接到握手完成之间，凭据可能被拒、节点可能未登记、服务端可能不可达。
    ///     没有它，上层只能一直显示"连接中"——连上了也看不出来。
    /// </remarks>
    public event EventHandler<int>? Handshaken;

    public async Task<ControlSessionResult> RunAsync(CancellationToken cancellationToken)
    {
        EventHandler<ControlNodeState> onStateChanged = (_, _) => SignalStateChanged();
        _stateStore.Changed += onStateChanged;

        try
        {
            return await RunCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateStore.Changed -= onStateChanged;
        }
    }

    private async Task<ControlSessionResult> RunCoreAsync(CancellationToken cancellationToken)
    {
        if (!await SendFrameAsync(BuildHelloFrame(), cancellationToken).ConfigureAwait(false))
            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, "hello_send_failed");

        ControlFrame? first;
        try
        {
            first = await ReceiveWithTimeoutAsync(_options.HandshakeTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new ControlSessionResult(ControlSessionEndReason.Stopped);
        }
        catch (Exception exception)
        {
            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, exception.GetType().Name);
        }

        if (first is null)
        {
            _logger.LogWarning("集控节点握手未收到 hello.ack（等待 {Seconds:0.#} 秒），断开重连。",
                _options.HandshakeTimeout.TotalSeconds);
            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, "handshake_timeout");
        }

        if (string.Equals(first.Type, ControlFrameTypes.Error, StringComparison.Ordinal))
            return ClassifyHandshakeError(first.Code);

        if (!string.Equals(first.Type, ControlFrameTypes.HelloAck, StringComparison.Ordinal))
        {
            // 协议要求第一帧是 hello.ack。收到别的类型说明对面不是我们认识的实现（或版本过新）：
            // 断开重连，让问题可见，而不是"连上了但一直没握手"地挂着。
            _logger.LogWarning("集控节点握手首帧不是 hello.ack：{Type}", first.Type);
            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, "unexpected_first_frame");
        }

        // 服务端下发的心跳间隔是它与反向代理 proxy_read_timeout 之间的唯一约定，必须照着用。
        NegotiatedHeartbeatSeconds = first.HeartbeatSeconds is > 0 and <= 3600
            ? first.HeartbeatSeconds.Value
            : _options.FallbackHeartbeatSeconds;

        _logger.LogInformation(
            "集控节点已握手：{NodeId}（组 {GroupId}，心跳 {Seconds} 秒，本地已应用 revision {Revision}）",
            _stateStore.Current.NodeId, _stateStore.Current.GroupId, NegotiatedHeartbeatSeconds,
            _stateStore.Current.AppliedDesiredStateRevision);

        // 通知上层"真的连上了"。订阅方抛异常不能带走连接循环。
        try
        {
            Handshaken?.Invoke(this, NegotiatedHeartbeatSeconds.Value);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "集控握手事件订阅方抛出异常。");
        }

        using var sessionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = RunHeartbeatAsync(NegotiatedHeartbeatSeconds.Value, sessionSource);

        var reason = ControlSessionEndReason.ConnectionLost;
        string? detail = null;

        try
        {
            while (!sessionSource.IsCancellationRequested)
            {
                ControlFrame? frame;
                try
                {
                    frame = await _transport.ReceiveAsync(sessionSource.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 心跳发送失败会取消会话：连接已经不健康。
                    detail = "heartbeat_failed";
                    break;
                }
                catch (OperationCanceledException)
                {
                    reason = ControlSessionEndReason.Stopped;
                    break;
                }
                catch (Exception exception)
                {
                    detail = exception.GetType().Name;
                    break;
                }

                if (frame is null)
                {
                    // 对端关闭时的原因（例如同一 node_id 被新连接接管 = replaced）要带出去：
                    // 协议里多数断开是静默的，"莫名掉线"和"有另一台机器用同一个 node_id"
                    // 必须能分辨，否则排查只能靠猜。
                    detail = _transport.CloseReason is { Length: > 0 } closeReason ? closeReason : "peer_closed";
                    break;
                }

                await DispatchAsync(frame, sessionSource.Token).ConfigureAwait(false);
            }
        }
        finally
        {
            await sessionSource.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 会话结束时的正常路径。
            }
        }

        if (cancellationToken.IsCancellationRequested)
            reason = ControlSessionEndReason.Stopped;

        _logger.LogInformation("集控节点连接结束：{Reason}（{Detail}）", reason, detail ?? "-");
        return new ControlSessionResult(reason, detail, HandshakeCompleted: true);
    }

    // ------------------------------------------------------------------ 心跳

    private async Task RunHeartbeatAsync(int heartbeatSeconds, CancellationTokenSource sessionSource)
    {
        var interval = TimeSpan.FromSeconds(heartbeatSeconds);

        while (!sessionSource.IsCancellationRequested)
        {
            // 先等一个心跳周期；期间本机状态有变化（开关、班级、已应用 revision）就立刻上报，
            // 不必等下一个周期——老师刚关掉本机开关时，控制台必须马上看到。
            await WaitForIntervalOrStateChangeAsync(interval, sessionSource.Token).ConfigureAwait(false);

            if (sessionSource.IsCancellationRequested)
                return;

            if (!await SendFrameAsync(BuildHeartbeatFrame(), sessionSource.Token).ConfigureAwait(false))
            {
                _logger.LogWarning("集控节点心跳发送失败，结束当前连接等待重连。");
                await sessionSource.CancelAsync().ConfigureAwait(false);
                return;
            }

            DrainStateSignal();
        }
    }

    private async Task WaitForIntervalOrStateChangeAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(interval, waitSource.Token);
        var signal = _stateChangedSignal.WaitAsync(waitSource.Token);

        var completed = await Task.WhenAny(delay, signal).ConfigureAwait(false);

        // 取消落败的一方；已完成的那个不会被取消影响。
        await waitSource.CancelAsync().ConfigureAwait(false);

        try
        {
            await completed.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 会话已结束。
        }
    }

    private void SignalStateChanged()
    {
        try
        {
            _stateChangedSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已经有一个待处理的信号，够了。
        }
    }

    private void DrainStateSignal()
    {
        while (_stateChangedSignal.Wait(0))
        {
            // 清空积压的信号：状态已经随刚才那一帧上报。
        }
    }

    // ------------------------------------------------------------------ 收帧

    private async Task DispatchAsync(ControlFrame frame, CancellationToken cancellationToken)
    {
        switch (frame.Type)
        {
            case ControlFrameTypes.Command:
                await HandleCommandAsync(frame, cancellationToken).ConfigureAwait(false);
                break;

            case ControlFrameTypes.DesiredState:
                ApplyDesiredState(frame);
                break;

            case ControlFrameTypes.Error:
                // 握手之后服务端仍可能发错误帧：记录下来继续跑，不因为一帧就断。
                _logger.LogWarning("集控节点收到服务端错误帧：{Code}", frame.Code);
                break;

            default:
                // 协议允许新增帧类型：未知帧一律忽略，不能崩。
                _logger.LogDebug("忽略未知集控帧类型：{Type}", frame.Type);
                break;
        }
    }

    // ------------------------------------------------------------------ 命令

    private async Task HandleCommandAsync(ControlFrame frame, CancellationToken cancellationToken)
    {
        var commandId = frame.CommandId;
        if (string.IsNullOrWhiteSpace(commandId))
        {
            // 没有 command_id 就无法回执，只能丢弃。
            _logger.LogWarning("集控命令缺少 command_id，已丢弃。");
            return;
        }

        // ① 过期检查必须最先，且必须在执行之前。
        //    网络乱序与重连补投都可能让一条过期命令到达；补执行三小时前的"立即抽取"
        //    在课间生效是完全不可接受的，所以这里**不依赖服务端已经拦过**。
        //    **缺失或解析不出来同样拒绝**：无法证明"还没过期"就不能执行（协议 §11 自查清单）；
        //    这一帧仍要回 ack，让控制台看到"设备拒绝了"而不是"设备掉线了"。
        if (frame.ExpiresAt is not { } expiresAt || _timeProvider.GetUtcNow() >= expiresAt)
        {
            _logger.LogInformation(
                "集控命令 {CommandId} 已过期或缺少可用的 expires_at（{ExpiresAt}），丢弃且不执行。",
                commandId, frame.ExpiresAt?.ToString("O") ?? "缺失/无法解析");
            await AckAsync(
                    commandId,
                    new ControlCommandAck(
                        false,
                        ControlRejectReasons.Expired,
                        // 缺 expires_at 与"确实过期"是两件事：前者是控制端没带，
                        // 后者是路上耽搁了。管理员看到的原因不应该一样。
                        ControlCommandOutcome.ToDetail(new
                        {
                            has_expires_at = frame.ExpiresAt is not null,
                            expires_at = frame.ExpiresAt?.ToString("O")
                        })),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // ② 本机开关：这是设备自己的闸，与服务端判定无关。关闭时拒绝一切动作命令。
        if (!_stateStore.Current.RemoteControlEnabled)
        {
            await AckAsync(
                    commandId,
                    new ControlCommandAck(
                        false,
                        ControlRejectReasons.LocalRemoteDisabled,
                        ControlCommandOutcome.ToDetail(new { remote_control_enabled = false })),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // ③ 能力自检：本机不真的支持就不接受，绝不"先收下再说"。
        var capability = frame.Capability ?? string.Empty;
        if (!_dispatcher.CanExecute(capability))
        {
            _logger.LogInformation("集控命令 {CommandId} 的能力不受支持：{Capability}", commandId, capability);
            await AckAsync(
                    commandId,
                    new ControlCommandAck(
                        false,
                        ControlRejectReasons.CapabilityUnsupported,
                        // 把本机能力清单带回去：控制台能直接说"这台机器不支持 X，它支持 Y Z"，
                        // 而不是让管理员去猜是不是权限问题。
                        ControlCommandOutcome.ToDetail(new
                        {
                            capability,
                            supported = _dispatcher.DeclaredCapabilities
                        })),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // ④ 幂等：同一条命令重复到达不重复执行，直接回上次的结果。
        if (_ledger.TryGet(commandId, out var recorded))
        {
            await SendRecordAsync(commandId, recorded, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⑤ 自行限流：服务端没有命令级限流（协议 §12 明确建议客户端防御），
        //    一条失控的连接不该把"立即抽取"变成连续触发。限流放在幂等之后，
        //    这样重复投递仍然只回放旧结果，不会被误判成洪峰。
        if (!TryAcceptWithinRateLimit())
        {
            _logger.LogWarning("集控命令 {CommandId} 超过本机限流窗口，已拒绝。", commandId);
            await AckAsync(
                    commandId,
                    new ControlCommandAck(
                        false,
                        ControlRejectReasons.RateLimited,
                        ControlCommandOutcome.ToDetail(new
                        {
                            max_commands = MaxCommandsPerWindow,
                            window_seconds = CommandRateWindow.TotalSeconds
                        })),
                    cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // 先 ack 再执行：在 ack 里做重活会让服务端以为命令丢了。
        var ack = new ControlCommandAck(true, null);
        await AckAsync(commandId, ack, cancellationToken).ConfigureAwait(false);

        ControlCommandResult result;
        try
        {
            var outcome = await _dispatcher
                .ExecuteAsync(new ControlCommandInvocation(commandId, capability, frame.Kind, frame.Payload), cancellationToken)
                .ConfigureAwait(false);
            result = new ControlCommandResult(outcome.Ok, outcome.Reason, outcome.Detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "集控命令 {CommandId} 执行失败。", commandId);
            result = new ControlCommandResult(
                false,
                ControlRejectReasons.ExecutionFailed,
                ControlCommandOutcome.ToDetail(new { exception = exception.GetType().Name }));
        }

        _ledger.Remember(commandId, new ControlCommandRecord(ack, result));
        await ResultAsync(commandId, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     本机命令限流：滑动窗口内最多接受 <see cref="MaxCommandsPerWindow" /> 条动作命令。
    /// </summary>
    /// <remarks>
    ///     服务端不限制"同一节点每秒多少条命令"（协议 §12），因此客户端必须自己防一手：
    ///     一条被攻破或写错的控制台连接否则可以无限触发抽取。
    /// </remarks>
    private bool TryAcceptWithinRateLimit()
    {
        var now = _timeProvider.GetUtcNow();

        while (_recentCommands.Count > 0 && now - _recentCommands.Peek() > CommandRateWindow)
            _recentCommands.Dequeue();

        if (_recentCommands.Count >= MaxCommandsPerWindow)
            return false;

        _recentCommands.Enqueue(now);
        return true;
    }

    private Task SendRecordAsync(string commandId, ControlCommandRecord record, CancellationToken cancellationToken)
    {
        // 回放上次的回执：重复的 action 命令既不能重复执行，也不该把服务端的状态往回改。
        return SendAckAndResultAsync(commandId, record.Ack, record.Result, cancellationToken);
    }

    private Task AckAsync(string commandId, ControlCommandAck ack, CancellationToken cancellationToken) =>
        SendFrameAsync(new ControlFrame
        {
            Type = ControlFrameTypes.Ack,
            CommandId = commandId,
            Accepted = ack.Accepted,
            Reason = ack.Reason,
            Detail = ack.Detail
        }, cancellationToken);

    private Task ResultAsync(string commandId, ControlCommandResult result, CancellationToken cancellationToken) =>
        SendFrameAsync(new ControlFrame
        {
            Type = ControlFrameTypes.Result,
            CommandId = commandId,
            Ok = result.Ok,
            Reason = result.Reason,
            Detail = result.Detail
        }, cancellationToken);

    private async Task SendAckAndResultAsync(
        string commandId,
        ControlCommandAck ack,
        ControlCommandResult result,
        CancellationToken cancellationToken)
    {
        await AckAsync(commandId, ack, cancellationToken).ConfigureAwait(false);
        await ResultAsync(commandId, result, cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ 期望状态

    /// <summary>
    ///     应用期望状态。规则只有一条：<c>incoming.revision &lt;= appliedRevision</c> 就丢弃。
    /// </summary>
    /// <remarks>
    ///     拒绝更小的 revision（即使它后到）让期望状态天然幂等、抗乱序、抗离线：
    ///     重连后服务端补投最新状态，据 revision 判断是否需要应用即可。
    ///     <c>appliedRevision</c> 会落盘——否则重启后归零，旧状态会被当成新状态应用。
    /// </remarks>
    private void ApplyDesiredState(ControlFrame frame)
    {
        var revision = frame.DesiredStateRevision ?? ControlDesiredState.NeverSetRevision;
        var applied = _stateStore.Current.AppliedDesiredStateRevision;

        if (revision <= applied)
        {
            _logger.LogDebug("忽略过期的期望状态：revision {Revision} ≤ 已应用 {Applied}", revision, applied);
            return;
        }

        // 载荷里只有 draw_locked 是当前协议定义的字段，其它字段一律忽略。
        // 即使这一帧没带可识别字段，也要记录 revision：revision 表达的是顺序，不是字段集合。
        var desired = ControlProtocolJson.ReadDesiredState(frame.Payload);
        _stateStore.Update(state => state with
        {
            AppliedDesiredStateRevision = revision,
            DrawLocked = desired?.DrawLocked ?? state.DrawLocked
        });

        _logger.LogInformation(
            "已应用期望状态 revision {Revision}：draw_locked={Locked}", revision, _stateStore.Current.DrawLocked);
    }

    // ------------------------------------------------------------------ 发送

    private ControlFrame BuildHelloFrame()
    {
        var state = _stateStore.Current;
        return new ControlFrame
        {
            Type = ControlFrameTypes.Hello,
            NodeId = state.NodeId,
            GroupId = state.GroupId,
            Platform = _options.Platform,
            Version = _options.Version,
            Capabilities = _dispatcher.DeclaredCapabilities,
            LocalRemoteAllowed = state.RemoteControlEnabled,
            // 用户填的名字优先，留空则上报主机名（见 ControlNodeDisplayName）。
            DisplayName = ControlNodeDisplayName.Resolve(state.DisplayName, _options.HostName),

            DesiredStateRevision = state.AppliedDesiredStateRevision
        };
    }

    private ControlFrame BuildHeartbeatFrame()
    {
        var state = _stateStore.Current;
        return new ControlFrame
        {
            Type = ControlFrameTypes.Heartbeat,
            Capabilities = _dispatcher.DeclaredCapabilities,
            LocalRemoteAllowed = state.RemoteControlEnabled,
            // 改了名字要随下一次心跳生效；状态变化本身会立刻唤醒一次心跳，不必等满间隔。
            DisplayName = ControlNodeDisplayName.Resolve(state.DisplayName, _options.HostName),
            DesiredStateRevision = state.AppliedDesiredStateRevision
        };
    }

    private async Task<bool> SendFrameAsync(ControlFrame frame, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "集控帧发送失败：{Type}", frame.Type);
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task<ControlFrame?> ReceiveWithTimeoutAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            return await _transport.ReceiveAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private ControlSessionResult ClassifyHandshakeError(string? code)
    {
        // 服务端会先发一帧可读错误再关闭连接——这不是网络故障，不能当成抖动来重试。
        switch (code)
        {
            case ControlErrorCodes.Unauthorized:
                return new ControlSessionResult(ControlSessionEndReason.Unauthorized, code);

            case ControlErrorCodes.InvalidRequest:
            case ControlErrorCodes.NodeNotFound:
            case ControlErrorCodes.GroupNotFound:
                _logger.LogWarning("集控节点握手被拒绝：{Code}。重试没有意义。", code);
                return new ControlSessionResult(ControlSessionEndReason.Terminal, code);

            default:
                // 未知错误码按"值得退避重试"处理：协议允许新增错误码，直接放弃会更糟。
                _logger.LogWarning("集控节点握手收到未知错误码：{Code}", code);
                return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, code ?? "unknown_error");
        }
    }
}
