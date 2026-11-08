using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     一条已建立的节点连接上的 v4.1 协议状态机。
/// </summary>
/// <remarks>
///     <para>
///         这一层不认识 WebSocket：它只依赖 <see cref="IControlNodeTransport" />，因此握手、心跳、
///         命令校验顺序、幂等与 <c>desired_state</c> 收敛都能在测试里用一个内存传输完整驱动。
///     </para>
///     <para>
///         <b>三种东西不能混为一谈</b>：期望状态（"应该是什么样"，不过期、靠 revision 收敛）、
///         动作命令（"做一次"，必须过期、过期即丢弃）。用队列模拟状态会重放，用状态模拟动作会永远触发。
///     </para>
///     <para>
///         回执是**两帧**：先 <c>cmd.ack</c>（愿不愿意执行），再 <c>cmd.result</c>（执行成功没有）。
///         两帧的 <c>id</c> 都是服务端给的 <c>command_id</c>；合并成一帧会掩盖"设备拒绝"与"设备执行失败"的区别。
///     </para>
/// </remarks>
public sealed class ControlNodeSession
{
    private readonly IControlNodeTransport _transport;
    private readonly IControlNodeStateStore _stateStore;
    private readonly IControlCommandDispatcher _dispatcher;
    private readonly IControlSettingsReportSource? _settingsReportSource;
    private readonly NodeDeviceCredential _credential;

    private readonly ControlNodeClientOptions _options;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly ControlCommandLedger _ledger;

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ControlV4Frame>> _pending = new(StringComparer.Ordinal);
    private readonly Queue<DateTimeOffset> _recentCommands = new();
    private int _requestSequence;

    /// <summary>最近一次告诉服务端的班级；<c>null</c> 表示还没说过或已释放。</summary>
    private string? _announcedClassId;

    private EventHandler<ControlNodeState>? _onStateChanged;

    private const int MaxCommandsPerWindow = 20;

    private static readonly TimeSpan CommandRateWindow = TimeSpan.FromSeconds(10);

    public ControlNodeSession(
        IControlNodeTransport transport,
        IControlNodeStateStore stateStore,
        IControlCommandDispatcher dispatcher,
        NodeDeviceCredential credential,
        ControlNodeClientOptions options,
        ILogger<ControlNodeSession> logger,
        TimeProvider? timeProvider = null,
        IControlSettingsReportSource? settingsReportSource = null)
    {
        _transport = transport;
        _stateStore = stateStore;
        _dispatcher = dispatcher;
        _settingsReportSource = settingsReportSource;
        _credential = credential;

        _options = options;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _ledger = new ControlCommandLedger(capacity: 512, timeToLive: TimeSpan.FromMinutes(30), _timeProvider);
    }

    /// <summary>服务端下发的心跳间隔；握手前为 <c>null</c>。</summary>
    public int? NegotiatedHeartbeatSeconds { get; private set; }

    /// <summary>服务端下发的离线判定阈值（秒）。</summary>
    public int? OfflineAfterSeconds { get; private set; }

    /// <summary>握手成功（收到 <c>res.ok:true</c>）时触发，参数是服务端下发的心跳间隔（秒）。</summary>
    public event EventHandler<int>? Handshaken;

    /// <summary>是否已完成 v4 握手；供设置页与测试观察。</summary>
    public bool IsHandshaken { get; private set; }

    public async Task<ControlSessionResult> RunAsync(CancellationToken cancellationToken)
    {
        using var sessionSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiveTask = ReceiveLoopAsync(sessionSource.Token);

        try
        {
            var hello = BuildHelloFrame();
            var response = await RequestAsync(hello, "hello-1", _options.HandshakeTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (response is null)
            {
                if (cancellationToken.IsCancellationRequested)
                    return new ControlSessionResult(ControlSessionEndReason.Stopped);

                _logger.LogWarning("集控节点握手未收到应答（等待 {Seconds:0.#} 秒），断开重连。",
                    _options.HandshakeTimeout.TotalSeconds);
                return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, "handshake_timeout");
            }

            if (response.Ok == false)
                return ClassifyHandshakeError(response.Code);

            var ack = ControlV4HelloAck.Parse(response.Payload);
            NegotiatedHeartbeatSeconds = ack.HeartbeatSeconds;
            OfflineAfterSeconds = ack.OfflineAfterSeconds;

            // 握手应答里的期望状态是"服务端当前认为的状态"，按同一套 revision 规则应用。
            if (ack.DesiredStateRevision > 0)
                ApplyDesiredState(new ControlV4DesiredState
                {
                    Revision = ack.DesiredStateRevision,
                    DrawLocked = ack.DrawLocked
                });

            _logger.LogInformation(
                "集控节点已握手：{DeviceId}（地点 {SiteId}，心跳 {Seconds} 秒，draw_locked={Locked}，revision {Revision}）",
                _credential.DeviceId, _credential.SiteId, NegotiatedHeartbeatSeconds,
                ack.DrawLocked, _stateStore.Current.AppliedDesiredStateRevision);

            IsHandshaken = true;

            // 握手成功的第一件事是「占班」：接入时若已经分过班，直接告诉服务端我在上课。
            // 不发这一帧的后果很具体：服务端占用表里没有这台设备 ⇒ 网页上班级详情看不到客户端，
            // 控制台的「允许抽取」开关按占用表广播 ⇒ 推不到这台机器 ⇒ 老师点了允许，远程抽取还是被本机拒。
            //
            // 每次重连都要重发，哪怕班级没变：占用表只活在 Hub 进程里，Hub 重启/发版后
            // 服务端那边已经空了，而本机 _announcedClassId 还记着「说过」⇒ 跳过 ⇒ 网页永远显示没人占班。
            _announcedClassId = null;
            await AnnounceSelectedClassAsync(cancellationToken).ConfigureAwait(false);

            // 用户中途改班级（设置页保存）也要跟上：订阅状态库的变化，只在班级真的变了时才发。
            _onStateChanged = (_, _) => _ = AnnounceSelectedClassAsync(CancellationToken.None);
            _stateStore.Changed += _onStateChanged;

            try
            {
                Handshaken?.Invoke(this, NegotiatedHeartbeatSeconds.Value);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "集控握手事件订阅方抛出异常。");
            }

            var heartbeatTask = RunHeartbeatAsync(NegotiatedHeartbeatSeconds.Value, sessionSource);

            var ended = await receiveTask.ConfigureAwait(false);
            await sessionSource.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 会话结束时的正常路径。
            }

            if (cancellationToken.IsCancellationRequested)
                return new ControlSessionResult(ControlSessionEndReason.Stopped, ended.Detail);

            // 关闭码 4003/4006 是"不要重连"的终态。
            var terminal = ended.TerminalCode ?? (_transport.CloseCode is 4003 or 4006
                ? _transport.CloseReason ?? (_transport.CloseCode == 4006 ? ControlV4Protocol.ErrorCodes.ProtocolTooOld : ControlV4Protocol.ErrorCodes.BadSecret)
                : null);
            if (terminal is not null)
                return new ControlSessionResult(ControlSessionEndReason.Terminal, terminal, HandshakeCompleted: true);

            return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, ended.Detail, HandshakeCompleted: true);
        }
        finally
        {
            await sessionSource.CancelAsync().ConfigureAwait(false);
            CancelPending("session_closed");

            if (_onStateChanged is not null)
            {
                _stateStore.Changed -= _onStateChanged;
                _onStateChanged = null;
            }

            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // receiveTask 的异常已经在上面的返回值里体现。
            }
        }
    }

    // ------------------------------------------------------------------ 收帧

    /// <summary>
    ///     告诉服务端「这台设备现在在哪个班上课」（占班 / 释放）。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         占班（<c>class.select</c>）= 上课 = 服务端把 <c>draw_locked=false</c> 推下来；
    ///         释放（<c>class.release</c>）= 下课 = 推 <c>draw_locked=true</c>。
    ///         所以这一帧不只是"上报"：它也是抽取锁的开关来源之一——不占班，控制台的
    ///         「允许抽取」开关按占用表广播，就推不到这台机器。
    ///     </para>
    ///     <para>
    ///         占班失败不影响连接：可能是别的设备刚占了这个班（服务端回 <c>busy</c>），
    ///         也可能是网络抖动。下一次状态变化或重连会再发一次。
    ///     </para>
    /// </remarks>
    private async Task AnnounceSelectedClassAsync(CancellationToken cancellationToken)
    {
        var classId = _stateStore.Current.ClassId;
        if (string.Equals(classId, _announcedClassId, StringComparison.Ordinal))
            return;

        _announcedClassId = classId;
        var id = "select-" + Interlocked.Increment(ref _requestSequence);
        var payload = new Dictionary<string, object?> { ["platform"] = ControlV4Protocol.Platform };
        if (!string.IsNullOrWhiteSpace(classId))
            payload["class_id"] = classId;

        var json = ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeRequest,
            Id = id,
            Topic = string.IsNullOrWhiteSpace(classId)
                ? ControlV4Protocol.Topics.ClassRelease
                : ControlV4Protocol.Topics.ClassSelect,
            Payload = ControlV4Json.ToElement(payload)
        });

        try
        {
            var response = await RequestAsync(json, id, TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);
            if (response is null)
            {
                _logger.LogDebug("占班请求没有应答（班级 {ClassId}），等下次状态变化或重连再试。",
                    classId ?? "未分班");
                return;
            }

            if (response.Ok == false)
            {
                _logger.LogInformation("占班未成功（班级 {ClassId}，原因 {Code}）：网页上这个班可能仍显示为无人。",
                    classId ?? "未分班", response.Code);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "占班请求发送失败，不影响连接本身。");
        }
    }

    private async Task<SessionEnd> ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? json;
            try
            {
                json = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return new SessionEnd("canceled", null);
            }
            catch (Exception exception)
            {
                return new SessionEnd(exception.GetType().Name, null);
            }

            if (json is null)
            {
                return new SessionEnd(
                    _transport.CloseReason is { Length: > 0 } reason ? reason : "peer_closed",
                    _transport.CloseCode is 4003 or 4006 ? _transport.CloseReason : null);
            }

            var frame = ControlV4Json.TryParse(json);
            if (frame is null)
            {
                _logger.LogDebug("集控节点收到无法解析的帧，已忽略。");
                continue;
            }

            switch (frame.Type)
            {
                case ControlV4Protocol.FrameTypeResponse:
                    if (!string.IsNullOrWhiteSpace(frame.Id)
                        && _pending.TryRemove(frame.Id, out var pending))
                    {
                        pending.TrySetResult(frame);
                    }

                    break;

                case ControlV4Protocol.FrameTypeEvent:
                    await DispatchEventAsync(frame, cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    _logger.LogDebug("忽略未知集控帧类型：{Type}", frame.Type);
                    break;
            }
        }

        return new SessionEnd("canceled", null);
    }

    private Task DispatchEventAsync(ControlV4Frame frame, CancellationToken cancellationToken)
    {
        switch (frame.Topic)
        {
            case ControlV4Protocol.Topics.Command:
                if (ControlV4Command.TryParse(frame.Payload, out var command))
                {
                    // 命令执行可能很慢（抽取、锁屏），绝不能在收帧循环里等它：
                    // 否则心跳应答与后续命令都会被堵住。
                    _ = Task.Run(() => HandleCommandAsync(command, cancellationToken), CancellationToken.None);
                }
                else
                {
                    _logger.LogWarning("集控命令载荷无效，已丢弃。");
                }

                break;

            case ControlV4Protocol.Topics.DesiredState:
                if (ControlV4DesiredState.TryParse(frame.Payload) is { } desired)
                    ApplyDesiredState(desired);
                else
                    _logger.LogWarning("集控期望状态载荷无效，已忽略。");
                break;

            case ControlV4Protocol.Topics.Evicted:
                var evicted = ReadEvicted(frame.Payload);
                _logger.LogWarning(
                    "集控节点被服务端驱逐：班级={ClassId} 原因={Reason} 操作者={By}。本机将清空班级并按锁定处理。",
                    evicted.ClassId ?? "-", evicted.Reason ?? "-", evicted.By ?? "-");

                // 被接管/替换/撤销后，本机不再持有那个班级。按 fail-closed 处理：
                // 清空班级并锁上抽取闸，绝不因为一条迟到的期望状态又把控制权抢回来。
                _stateStore.Update(state => state with { ClassId = null, DrawLocked = true });
                break;

            case ControlV4Protocol.Topics.Error:
                _logger.LogWarning("集控节点收到服务端错误帧：{Code}", frame.Code);
                break;

            default:
                _logger.LogDebug("忽略未知集控事件：{Topic}", frame.Topic);
                break;
        }

        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ 命令

    private async Task HandleCommandAsync(ControlV4Command command, CancellationToken cancellationToken)
    {
        var commandId = command.CommandId;

        // ① 过期检查必须最先，且必须在执行之前；缺失 expires_at 同样拒绝。
        //    v4 的 expires_at 是毫秒时间戳，不是 ISO 字符串。
        if (command.ExpiresAt is not { } expiresAtMs
            || _timeProvider.GetUtcNow().ToUnixTimeMilliseconds() >= expiresAtMs)
        {
            _logger.LogInformation(
                "集控命令 {CommandId} 已过期或缺少可用的 expires_at（{ExpiresAt}），丢弃且不执行。",
                commandId, command.ExpiresAt?.ToString() ?? "缺失/无法解析");
            await AckAsync(commandId, false, ControlV4Protocol.ErrorCodes.Expired, new
            {
                has_expires_at = command.ExpiresAt is not null,
                expires_at = command.ExpiresAt
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ② 本机开关：设备自己的闸，关闭时拒绝一切动作命令。
        if (!_stateStore.Current.RemoteControlEnabled)
        {
            await AckAsync(commandId, false, ControlV4Protocol.ErrorCodes.LocalRemoteDisabled,
                new { remote_control_enabled = false }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var capability = command.Cmd;

        // ③ 能力自检：本机不真的支持就不接受。
        if (!_dispatcher.CanExecute(capability))
        {
            _logger.LogInformation("集控命令 {CommandId} 的能力不受支持：{Capability}", commandId, capability);
            await AckAsync(commandId, false, ControlV4Protocol.ErrorCodes.CapabilityUnsupported, new
            {
                capability,
                supported = _dispatcher.DeclaredCapabilities
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ④ 幂等：重复到达不重复执行，直接回上次的回执。
        if (_ledger.TryGet(commandId, out var recorded))
        {
            await SendAckAndResultAsync(commandId, recorded, cancellationToken).ConfigureAwait(false);
            return;
        }

        // ⑤ 自行限流：服务端没有命令级限流。
        if (!TryAcceptWithinRateLimit())
        {
            _logger.LogWarning("集控命令 {CommandId} 超过本机限流窗口，已拒绝。", commandId);
            await AckAsync(commandId, false, ControlV4Protocol.ErrorCodes.RateLimited, new
            {
                max_commands = MaxCommandsPerWindow,
                window_seconds = CommandRateWindow.TotalSeconds
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var ack = new ControlCommandAck(true, null);
        await AckAsync(commandId, true, null, null, cancellationToken).ConfigureAwait(false);

        ControlCommandResult result;
        try
        {
            var outcome = await _dispatcher
                .ExecuteAsync(new ControlCommandInvocation(commandId, capability, null, command.Args), cancellationToken)
                .ConfigureAwait(false);
            result = new ControlCommandResult(outcome.Ok, outcome.Reason, outcome.Detail, outcome.Payload);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "集控命令 {CommandId} 执行失败。", commandId);
            result = new ControlCommandResult(false, ControlV4Protocol.ErrorCodes.ExecutionFailed,
                ControlCommandOutcome.ToDetail(new { exception = exception.GetType().Name }));
        }

        _ledger.Remember(commandId, new ControlCommandRecord(ack, result));
        await ResultAsync(commandId, result, cancellationToken).ConfigureAwait(false);
    }

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

    // ------------------------------------------------------------------ 期望状态

    /// <summary>应用期望状态。规则只有一条：<c>incoming.revision &lt;= appliedRevision</c> 就丢弃。</summary>
    /// <remarks>
    ///     拒绝更小的 revision（即使它后到）让期望状态天然幂等、抗乱序、抗离线。
    ///     <c>appliedRevision</c> 必须落盘，否则重启后归零，旧状态会被当成新状态应用。
    ///     无法证明服务端允许抽取时（<c>draw_locked</c> 缺失）必须按锁定处理。
    /// </remarks>
    private void ApplyDesiredState(ControlV4DesiredState desired)
    {
        var applied = _stateStore.Current.AppliedDesiredStateRevision;
        if (desired.Revision <= applied)
        {
            _logger.LogDebug("忽略过期的期望状态：revision {Revision} ≤ 已应用 {Applied}", desired.Revision, applied);
            return;
        }

        _stateStore.Update(state => state with
        {
            AppliedDesiredStateRevision = desired.Revision,
            DrawLocked = desired.DrawLocked ?? true,
            ClassId = desired.ClassId
        });

        _logger.LogInformation(
            "已应用期望状态 revision {Revision}：draw_locked={Locked}，class_id={ClassId}",
            desired.Revision, _stateStore.Current.DrawLocked, desired.ClassId ?? "未分班");
    }

    // ------------------------------------------------------------------ 发送

    /// <summary>驱逐帧的字段名在协议里是 camelCase，但容忍 snake_case 的旧写法。</summary>
    private static (string? ClassId, string? Reason, string? By) ReadEvicted(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
            return (null, null, null);

        string? Read(params string[] names)
        {
            foreach (var name in names)
                if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    return value.GetString();
            return null;
        }

        return (Read("class_id", "classId"), Read("reason"), Read("by"));
    }

    private string BuildHelloFrame()
    {
        var state = _stateStore.Current;
        var hello = new ControlV4Hello
        {
            DeviceId = _credential.DeviceId,
            Secret = _credential.Secret,
            // 用户填的名字优先，留空则上报主机名（见 ControlNodeDisplayName）。
            DeviceName = ControlNodeDisplayName.Resolve(state.DisplayName, _options.HostName)
                         ?? Environment.MachineName,
            Version = _options.Version,
            Capabilities = _dispatcher.DeclaredCapabilities,
            AppliedStateRevision = state.AppliedDesiredStateRevision,
            // v4.2：设置 schema 随 hello 上报。生成失败只让这一帧缺 settings，不影响握手。
            Settings = _settingsReportSource?.Build()
        };

        return ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeRequest,
            Id = "hello-1",
            Topic = ControlV4Protocol.Topics.Hello,
            Payload = ControlV4Json.ToElement(hello)
        });
    }

    private async Task RunHeartbeatAsync(int heartbeatSeconds, CancellationTokenSource sessionSource)
    {
        var interval = TimeSpan.FromSeconds(heartbeatSeconds);

        while (!sessionSource.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, sessionSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var id = "ping-" + Interlocked.Increment(ref _requestSequence);
            var response = await RequestAsync(
                    ControlV4Json.Serialize(new ControlV4Frame
                    {
                        Type = ControlV4Protocol.FrameTypeRequest,
                        Id = id,
                        Topic = ControlV4Protocol.Topics.Ping
                    }),
                    id,
                    interval,
                    sessionSource.Token)
                .ConfigureAwait(false);

            if (response is not null)
                continue;

            if (sessionSource.IsCancellationRequested)
                return;

            // 一次心跳没答还可以是网络抖动；连续两次没答（2 × heartbeat_seconds）才判定连接不健康。
            _logger.LogWarning("集控心跳无应答，再等一个周期。");
            try
            {
                await Task.Delay(interval, sessionSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (sessionSource.IsCancellationRequested)
                return;

            _logger.LogWarning("集控心跳连续两次无应答，结束当前连接等待重连。");
            await sessionSource.CancelAsync().ConfigureAwait(false);
            return;
        }
    }

    private async Task<ControlV4Frame?> RequestAsync(
        string frameJson,
        string id,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ControlV4Frame>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            return null;

        try
        {
            if (!await SendFrameAsync(frameJson, cancellationToken).ConfigureAwait(false))
                return null;

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            var completed = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(Timeout.Infinite, timeoutSource.Token))
                .ConfigureAwait(false);

            if (completed != completion.Task)
                return null;

            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task AckAsync(
        string commandId,
        bool accepted,
        string? code,
        object? detail,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?> { ["accepted"] = accepted };
        if (code is not null)
            payload["code"] = code;
        if (detail is not null)
            payload["detail"] = ControlCommandOutcome.ToDetail(detail);

        return SendFrameAsync(ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeRequest,
            Id = commandId,
            Topic = ControlV4Protocol.Topics.CommandAck,
            Payload = ControlV4Json.ToElement(payload)
        }), cancellationToken);
    }

    private Task ResultAsync(string commandId, ControlCommandResult result, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?> { ["ok"] = result.Ok };
        if (!result.Ok && result.Reason is not null)
            payload["code"] = result.Reason;
        if (result.Detail is not null)
            payload["detail"] = result.Detail.Value;
        if (result.Payload is not null)
            payload["data"] = result.Payload.Value;

        return SendFrameAsync(ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeRequest,
            Id = commandId,
            Topic = ControlV4Protocol.Topics.CommandResult,
            Payload = ControlV4Json.ToElement(payload)
        }), cancellationToken);
    }

    private async Task SendAckAndResultAsync(
        string commandId,
        ControlCommandRecord record,
        CancellationToken cancellationToken)
    {
        await AckAsync(commandId, record.Ack.Accepted, record.Ack.Reason, record.Ack.Detail, cancellationToken)
            .ConfigureAwait(false);
        await ResultAsync(commandId, record.Result, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SendFrameAsync(string frameJson, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _transport.SendAsync(frameJson, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ControlFrameTooLargeException exception)
        {
            _logger.LogWarning(exception, "集控帧超过帧上限，无法发送。");
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "集控帧发送失败。");
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void CancelPending(string reason)
    {
        foreach (var key in _pending.Keys)
        {
            if (_pending.TryRemove(key, out var pending))
                pending.TrySetCanceled(new CancellationToken(canceled: true));
        }
    }

    private ControlSessionResult ClassifyHandshakeError(string? code)
    {
        switch (code)
        {
            case ControlV4Protocol.ErrorCodes.BadSecret:
            case ControlV4Protocol.ErrorCodes.DeviceRevoked:
            case ControlV4Protocol.ErrorCodes.ProtocolTooOld:
            case ControlV4Protocol.ErrorCodes.NotAuthed:
                _logger.LogWarning("集控节点握手被拒绝：{Code}。重试没有意义。", code);
                return new ControlSessionResult(ControlSessionEndReason.Terminal, code);

            case ControlV4Protocol.ErrorCodes.BadRequest:
                _logger.LogWarning("集控节点握手请求无效：{Code}。重试没有意义。", code);
                return new ControlSessionResult(ControlSessionEndReason.Terminal, code);

            default:
                // 未知错误码按"值得退避重试"处理：协议允许新增错误码。
                _logger.LogWarning("集控节点握手收到未知错误码：{Code}", code);
                return new ControlSessionResult(ControlSessionEndReason.ConnectionLost, code ?? "unknown_error");
        }
    }

    private readonly record struct SessionEnd(string? Detail, string? TerminalCode);
}