using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     Mirrors the protocol checklist in <c>docs/client-protocol.md</c> for the node channel, driven by an
///     in-memory transport so the ordering rules (expiry before everything, ack before execution, revision
///     monotonicity) are asserted without a live server.
/// </summary>
public sealed class ControlNodeProtocolTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>测试里的"主机名"：显示名留空时就该上报它。</summary>
    private const string TestHostName = "sr-test-host";

    private static ControlNodeClientOptions Options(TimeSpan? handshakeTimeout = null) => new()
    {
        Platform = "windows",
        Version = "v3.1.2",
        HandshakeTimeout = handshakeTimeout ?? TimeSpan.FromSeconds(5),
        // 回落值固定下来，断言才不依赖跑测试那台机器的主机名。
        HostName = TestHostName
    };

    private static ControlNodeState State(bool remoteControlEnabled = true, string? displayName = null) => new()
    {
        NodeId = "b3f1c2d4e5a6",
        GroupId = "grp_9f8e7d6c5b4a",
        RemoteControlEnabled = remoteControlEnabled,
        DisplayName = displayName
    };

    private static (ControlNodeSession Session, FakeNodeTransport Transport, InMemoryStateStore Store, RecordingDispatcher Dispatcher)
        CreateSession(
            ControlNodeState? state = null,
            RecordingDispatcher? dispatcher = null,
            TimeProvider? timeProvider = null,
            TimeSpan? handshakeTimeout = null)
    {
        var transport = new FakeNodeTransport();
        var store = new InMemoryStateStore(state ?? State());
        var commands = dispatcher ?? new RecordingDispatcher();
        var session = new ControlNodeSession(
            transport,
            store,
            commands,
            Options(handshakeTimeout),
            NullLogger<ControlNodeSession>.Instance,
            timeProvider);

        return (session, transport, store, commands);
    }

    private static ControlFrame HelloAck(int heartbeatSeconds = 3600) => new()
    {
        Type = ControlFrameTypes.HelloAck,
        HeartbeatSeconds = heartbeatSeconds,
        OfflineAfterSeconds = 120,
        DesiredStateRevision = 0
    };

    private static ControlFrame Command(
        string capability,
        DateTimeOffset? expiresAt = null,
        string commandId = "cmd_1",
        string kind = "action",
        object? payload = null) => new()
    {
        Type = ControlFrameTypes.Command,
        CommandId = commandId,
        Capability = capability,
        Kind = kind,
        Payload = payload is null ? null : JsonSerializer.SerializeToElement(payload),
        ExpiresAt = expiresAt
    };

    private static async Task<ControlSessionResult> RunHandshakeAsync(
        ControlNodeSession session,
        FakeNodeTransport transport,
        ControlFrame? ack = null)
    {
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(ack ?? HelloAck());
        return await runTask;
    }

    private static Task<ControlSessionResult> RunAsync(ControlNodeSession session, FakeNodeTransport transport) =>
        RunAsync(session, transport, Timeout);

    private static async Task<ControlSessionResult> RunAsync(
        ControlNodeSession session,
        FakeNodeTransport transport,
        TimeSpan timeout)
    {
        using var source = new CancellationTokenSource(timeout);
        var task = session.RunAsync(source.Token);

        // The session ends when the transport is closed by the test.
        return await task;
    }

    // ------------------------------------------------------------------ 握手

    [Fact]
    public async Task Hello_IsTheFirstFrame_AndCarriesIdentityCapabilitiesAndLocalSwitch()
    {
        var (session, transport, _, _) = CreateSession(State(remoteControlEnabled: false));

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        var hello = transport.Sent[0];
        Assert.Equal(ControlFrameTypes.Hello, hello.Type);
        Assert.Equal("b3f1c2d4e5a6", hello.NodeId);
        Assert.Equal("grp_9f8e7d6c5b4a", hello.GroupId);
        Assert.Equal("windows", hello.Platform);
        Assert.Equal("v3.1.2", hello.Version);
        Assert.False(hello.LocalRemoteAllowed);
        Assert.Equal(ControlDesiredState.NeverSetRevision, hello.DesiredStateRevision);
        // 显示名留空 ⇒ 上报主机名：控制台里不该只看到一串随机 node_id。
        Assert.Equal(TestHostName, hello.DisplayName);
        Assert.Equal(["node.status.read", "draw.lock", "draw.trigger"], hello.Capabilities);
        // 班级名（名单名）**不上报**：控制平面不需要知道这间教室在上哪个班的课。
        // 钉住它是因为"顺手把当前名单名加进 hello"看起来很像无害的便利改进。
        Assert.Null(hello.CurrentClass);

        transport.Push(HelloAck());
        await Task.Delay(50);
        transport.Close();
        Assert.Equal(ControlSessionEndReason.ConnectionLost, (await runTask).Reason);
    }

    [Theory]
    [InlineData(ControlErrorCodes.InvalidRequest)]
    [InlineData(ControlErrorCodes.NodeNotFound)]
    [InlineData(ControlErrorCodes.GroupNotFound)]
    public async Task HandshakeError_IsTerminal_AndMustNotBeRetried(string code)
    {
        var (session, transport, _, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        transport.Push(new ControlFrame { Type = ControlFrameTypes.Error, Code = code });

        var result = await runTask;
        Assert.Equal(ControlSessionEndReason.Terminal, result.Reason);
        Assert.Equal(code, result.Detail);
        Assert.False(result.HandshakeCompleted);
    }

    [Fact]
    public async Task HandshakeError_Unauthorized_IsRetryableAfterCredentialRefresh()
    {
        var (session, transport, _, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        transport.Push(new ControlFrame { Type = ControlFrameTypes.Error, Code = ControlErrorCodes.Unauthorized });

        Assert.Equal(ControlSessionEndReason.Unauthorized, (await runTask).Reason);
    }

    [Fact]
    public async Task HandshakeWithoutHelloAck_EndsTheSession()
    {
        var (session, transport, _, _) = CreateSession(handshakeTimeout: TimeSpan.FromMilliseconds(150));
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        var result = await runTask;
        Assert.Equal(ControlSessionEndReason.ConnectionLost, result.Reason);
        Assert.Equal("handshake_timeout", result.Detail);
    }

    /// <summary>
    ///     握手成功必须**触发事件**，否则上层只能一直显示"连接中"：
    ///     发起连接 ≠ 连上（凭据可能被拒、节点可能未登记）。
    /// </summary>
    [Fact]
    public async Task HandshakeRaisesTheHandshakenEvent_SoTheUiCanShowConnected()
    {
        var (session, transport, _, _) = CreateSession();
        var seen = new List<int>();
        session.Handshaken += (_, seconds) => seen.Add(seconds);

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        Assert.Empty(seen);
        transport.Push(HelloAck(heartbeatSeconds: 1));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Heartbeat, TimeSpan.FromSeconds(5));

        Assert.Equal([1], seen);

        transport.Close();
        await runTask;
    }

    /// <summary>
    ///     失败结果必须**带上结构化 detail**：只回一个原因码，控制台就只能显示一个单词，
    ///     管理员既不知道具体是什么情况，也不知道下一步该做什么。
    /// </summary>
    [Fact]
    public async Task CommandFailure_CarriesStructuredDetailOntoTheWire()
    {
        var dispatcher = new RecordingDispatcher
        {
            Handler = _ => ControlCommandOutcome.Failure(
                "media_disabled",
                new { voice_enable = false })
        };
        var (session, transport, _, _) = CreateSession(dispatcher: dispatcher);

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck(heartbeatSeconds: 1));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Heartbeat);

        transport.Push(Command(
            ControlCapabilities.DrawTrigger,
            expiresAt: TimeProvider.System.GetUtcNow().AddMinutes(5)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Result);
        var result = transport.SentOfType(ControlFrameTypes.Result).Single();

        Assert.False(result.Ok);
        Assert.Equal("media_disabled", result.Reason);
        Assert.NotNull(result.Detail);
        Assert.False(result.Detail!.Value.GetProperty("voice_enable").GetBoolean());

        transport.Close();
        await runTask;
    }

    /// <summary>
    ///     拒绝（ack=false）同样要带 detail：例如"本机远控开关关着"或"不支持这条能力"，
    ///     光一个 local_remote_disabled / capability_unsupported 看不出是谁的问题。
    /// </summary>
    [Fact]
    public async Task AckRejection_CarriesStructuredDetailOntoTheWire()
    {
        // 本机开关关着：这是设备自己的闸，命令在能力检查之前就被拒。
        var (session, transport, _, _) = CreateSession(State(remoteControlEnabled: false));

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck(heartbeatSeconds: 1));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Heartbeat);

        transport.Push(Command(
            ControlCapabilities.DrawTrigger,
            expiresAt: TimeProvider.System.GetUtcNow().AddMinutes(5)));

        await transport.WaitForSentAsync(
            frame => frame.Type == ControlFrameTypes.Ack && frame.Accepted == false);

        var ack = transport.SentOfType(ControlFrameTypes.Ack).Single();

        Assert.Equal(ControlRejectReasons.LocalRemoteDisabled, ack.Reason);
        Assert.NotNull(ack.Detail);
        Assert.False(ack.Detail!.Value.GetProperty("remote_control_enabled").GetBoolean());

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task HeartbeatInterval_ComesFromTheServer_NotFromTheClient()
    {
        var (session, transport, _, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck(heartbeatSeconds: 1));

        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Heartbeat, TimeSpan.FromSeconds(5));
        Assert.Equal(1, session.NegotiatedHeartbeatSeconds);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task LocalSwitchChange_IsReportedOnTheNextHeartbeatWithoutWaitingForTheInterval()
    {
        var (session, transport, store, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        // 一小时的间隔意味着这一帧只能来自"状态变化立刻上报"，不是周期到了。
        transport.Push(HelloAck(heartbeatSeconds: 3600));
        await Task.Delay(100);

        var before = transport.Sent.Count(frame => frame.Type == ControlFrameTypes.Heartbeat);
        store.Update(state => state with { RemoteControlEnabled = false });

        await transport.WaitForSentAsync(
            frame => frame.Type == ControlFrameTypes.Heartbeat && frame.LocalRemoteAllowed == false,
            TimeSpan.FromSeconds(5));

        Assert.True(transport.Sent.Count(frame => frame.Type == ControlFrameTypes.Heartbeat) > before);

        transport.Close();
        await runTask;
    }

    /// <summary>
    ///     用户在设置页填了显示名就上报它，**优先于主机名**（见 <c>ControlNodeDisplayName</c>）。
    /// </summary>
    [Fact]
    public async Task ConfiguredDisplayName_WinsOverTheHostName_OnHelloAndHeartbeat()
    {
        var (session, transport, _, _) = CreateSession(State(displayName: "301班讲台机"));

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        Assert.Equal("301班讲台机", transport.Sent[0].DisplayName);

        // 心跳带同一个名字：控制台里改名后不必重连也能看到。
        // 用 1 秒间隔让**周期**心跳真的发出来（间隔 3600 就只能靠状态变化唤醒）。
        transport.Push(HelloAck(heartbeatSeconds: 1));
        await transport.WaitForSentAsync(
            frame => frame.Type == ControlFrameTypes.Heartbeat,
            TimeSpan.FromSeconds(5));

        Assert.All(
            transport.SentOfType(ControlFrameTypes.Heartbeat),
            frame => Assert.Equal("301班讲台机", frame.DisplayName));

        transport.Close();
        await runTask;
    }

    /// <summary>
    ///     改名要走"状态变化立刻上报"，不必等满一个心跳间隔——否则用户改完名字在控制台上
    ///     半天看不到变化，会以为没生效。
    /// </summary>
    [Fact]
    public async Task DisplayNameChange_IsReportedOnTheNextHeartbeatWithoutWaitingForTheInterval()
    {
        var (session, transport, store, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        transport.Push(HelloAck(heartbeatSeconds: 3600));
        await Task.Delay(100);

        store.Update(state => state with { DisplayName = "改过的名字" });

        await transport.WaitForSentAsync(
            frame => frame.Type == ControlFrameTypes.Heartbeat && frame.DisplayName == "改过的名字",
            TimeSpan.FromSeconds(5));

        transport.Close();
        await runTask;
    }

    // ------------------------------------------------------------------ 命令

    [Fact]
    public async Task ExpiredCommand_IsDroppedAndNeverExecuted()
    {
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        var expired = Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(-5));
        transport.Push(expired);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        var ack = transport.SentOfType(ControlFrameTypes.Ack).Single();
        Assert.False(ack.Accepted);
        Assert.Equal(ControlRejectReasons.Expired, ack.Reason);
        Assert.Empty(dispatcher.Invocations);
        Assert.Empty(transport.SentOfType(ControlFrameTypes.Result));

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task CommandWithoutUsableExpiresAt_IsRejectedInsteadOfExecuted()
    {
        // 协议 §11 自查清单：expires_at **解析失败也拒绝**。
        // 缺失与无法解析对节点是同一件事——无法证明"还没过期"就不能执行，
        // 但仍然要回 ack，让控制台看到"设备拒绝了"而不是"设备掉线了"。
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.Command,
            CommandId = "cmd_no_expiry",
            Capability = ControlCapabilities.DrawTrigger
        });

        var malformed = ControlProtocolJson.TryParse(
            """{"type":"command","command_id":"cmd_bad_expiry","capability":"draw.trigger","expires_at":"not-a-date"}""");
        Assert.NotNull(malformed);
        Assert.Null(malformed!.ExpiresAt);
        transport.Push(malformed);

        await transport.WaitForSentAsync(
            _ => transport.SentOfType(ControlFrameTypes.Ack).Count == 2,
            TimeSpan.FromSeconds(5));

        Assert.Empty(dispatcher.Invocations);
        Assert.All(
            transport.SentOfType(ControlFrameTypes.Ack),
            ack => Assert.Equal(ControlRejectReasons.Expired, ack.Reason));

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task ServerCloseReason_IsSurfacedInTheSessionResult()
    {
        // 同一 node_id 被新连接接管时，服务端会带 reason=replaced 关闭旧连接（协议 §5）。
        // 这条原因必须能带出来，否则"莫名掉线"与"两台机器共用一个 node_id"分不清。
        var (session, transport, _, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Close("replaced");

        var result = await runTask;
        Assert.Equal(ControlSessionEndReason.ConnectionLost, result.Reason);
        Assert.Equal("replaced", result.Detail);
    }

    [Fact]
    public async Task ExpiryIsCheckedBeforeTheLocalSwitch()
    {
        // §6.1：过期检查必须在第 2、3 步之前——过期即丢弃，理由也只能是 expired。
        var (session, transport, _, _) = CreateSession(State(remoteControlEnabled: false));
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(-1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        Assert.Equal(ControlRejectReasons.Expired, transport.SentOfType(ControlFrameTypes.Ack).Single().Reason);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task LocalSwitchOff_RejectsEveryActionCommand()
    {
        var (session, transport, _, dispatcher) = CreateSession(State(remoteControlEnabled: false));
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        var ack = transport.SentOfType(ControlFrameTypes.Ack).Single();
        Assert.False(ack.Accepted);
        Assert.Equal(ControlRejectReasons.LocalRemoteDisabled, ack.Reason);
        Assert.Empty(dispatcher.Invocations);

        transport.Close();
        await runTask;
    }

    [Theory]
    [InlineData("evil.capability")]
    // draw.lock 是期望状态：声明它，但拒绝它的动作形式，避免服务端状态与本机实际状态分叉。
    [InlineData(ControlCapabilities.DrawLock)]
    [InlineData(ControlCapabilities.RosterWrite)]
    public async Task UnsupportedCapability_IsRejectedWithoutExecution(string capability)
    {
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(Command(capability, DateTimeOffset.UtcNow.AddMinutes(1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        var ack = transport.SentOfType(ControlFrameTypes.Ack).Single();
        Assert.False(ack.Accepted);
        Assert.Equal(ControlRejectReasons.CapabilityUnsupported, ack.Reason);
        Assert.Empty(dispatcher.Invocations);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task AcceptedCommand_IsAcknowledgedBeforeExecution_AndResultsAreTwoFrames()
    {
        var dispatcher = new RecordingDispatcher();
        var executionObservedAfterAck = false;
        dispatcher.Handler = invocation =>
        {
            executionObservedAfterAck = true;
            return ControlCommandOutcome.Success;
        };

        var (session, transport, _, _) = CreateSession(dispatcher: dispatcher);
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Result, TimeSpan.FromSeconds(2));

        var frames = transport.Sent.Where(frame =>
                frame.Type is ControlFrameTypes.Ack or ControlFrameTypes.Result)
            .ToList();

        Assert.True(executionObservedAfterAck);
        Assert.Equal(2, frames.Count);
        Assert.Equal(ControlFrameTypes.Ack, frames[0].Type);
        Assert.True(frames[0].Accepted);
        Assert.Equal(ControlFrameTypes.Result, frames[1].Type);
        Assert.True(frames[1].Ok);
        Assert.Equal("cmd_1", frames[0].CommandId);
        Assert.Equal("cmd_1", frames[1].CommandId);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task DuplicateCommandId_IsNotExecutedTwice_AndReplaysTheRecordedResult()
    {
        var dispatcher = new RecordingDispatcher();
        var calls = 0;
        dispatcher.Handler = _ =>
        {
            calls++;
            return ControlCommandOutcome.Success;
        };

        var (session, transport, _, _) = CreateSession(dispatcher: dispatcher);
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        var command = Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1));
        transport.Push(command);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Result);
        transport.Push(command);
        await transport.WaitForSentAsync(
            _ => transport.Sent.Count(frame => frame.Type == ControlFrameTypes.Result) == 2,
            TimeSpan.FromSeconds(2));

        Assert.Equal(1, calls);
        var acks = transport.SentOfType(ControlFrameTypes.Ack);
        var results = transport.SentOfType(ControlFrameTypes.Result);
        Assert.Equal(2, acks.Count);
        Assert.Equal(2, results.Count);
        Assert.All(acks, ack => Assert.True(ack.Accepted));
        Assert.All(results, result => Assert.True(result.Ok));

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task CommandFlood_IsRateLimitedLocally()
    {
        // 服务端没有命令级限流（协议 §12）：客户端必须自己挡住失控的连接。
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        for (var index = 0; index < 21; index++)
        {
            transport.Push(Command(
                ControlCapabilities.DrawTrigger,
                DateTimeOffset.UtcNow.AddMinutes(1),
                commandId: $"cmd_{index}"));
        }

        await transport.WaitForSentAsync(
            _ => transport.SentOfType(ControlFrameTypes.Ack).Count == 21,
            TimeSpan.FromSeconds(10));

        Assert.Equal(20, dispatcher.Invocations.Count);
        Assert.Equal(20, transport.SentOfType(ControlFrameTypes.Result).Count);
        Assert.Equal(ControlRejectReasons.RateLimited, transport.SentOfType(ControlFrameTypes.Ack)[^1].Reason);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task FailingExecution_StillReportsAckThenAFailedResult()
    {
        var dispatcher = new RecordingDispatcher
        {
            Handler = _ => ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed)
        };

        var (session, transport, _, _) = CreateSession(dispatcher: dispatcher);
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Result);

        Assert.True(transport.SentOfType(ControlFrameTypes.Ack).Single().Accepted);
        var result = transport.SentOfType(ControlFrameTypes.Result).Single();
        Assert.False(result.Ok);
        Assert.Equal(ControlRejectReasons.ExecutionFailed, result.Reason);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task CommandWithoutCommandId_IsDropped()
    {
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.Command,
            CommandId = null,
            Capability = ControlCapabilities.DrawTrigger,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1)
        });

        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1), commandId: "cmd_2"));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        Assert.Single(dispatcher.Invocations);
        Assert.Equal("cmd_2", dispatcher.Invocations[0].CommandId);

        transport.Close();
        await runTask;
    }

    // ------------------------------------------------------------------ 期望状态

    [Fact]
    public async Task DesiredState_OnlyAppliesStrictlyNewerRevisions_AndPersistsTheResult()
    {
        var (session, transport, store, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.DesiredState,
            DesiredStateRevision = 1_759_572_000_123,
            Payload = JsonSerializer.SerializeToElement(new { draw_locked = true })
        });
        await store.WaitForRevisionAsync(1_759_572_000_123);

        Assert.True(store.Current.DrawLocked);
        Assert.Equal(1, store.SaveCount);

        // 更小的 revision 即使后到也必须丢弃（抗乱序、抗离线补投）。
        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.DesiredState,
            DesiredStateRevision = 1_759_572_000_000,
            Payload = JsonSerializer.SerializeToElement(new { draw_locked = false })
        });
        await Task.Delay(100);

        Assert.True(store.Current.DrawLocked);
        Assert.Equal(1_759_572_000_123, store.Current.AppliedDesiredStateRevision);

        // 相同的 revision 同样丢弃（重复投递无害）。
        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.DesiredState,
            DesiredStateRevision = 1_759_572_000_123,
            Payload = JsonSerializer.SerializeToElement(new { draw_locked = false })
        });
        await Task.Delay(100);

        Assert.True(store.Current.DrawLocked);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task DesiredState_IgnoresUnknownPayloadFields_ButStillAdvancesTheRevision()
    {
        var (session, transport, store, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame
        {
            Type = ControlFrameTypes.DesiredState,
            DesiredStateRevision = 42,
            Payload = JsonSerializer.SerializeToElement(new { future_flag = true })
        });
        await store.WaitForRevisionAsync(42);

        Assert.False(store.Current.DrawLocked);
        Assert.Equal(42, store.Current.AppliedDesiredStateRevision);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task ReconnectReportsTheLocalAppliedRevision_InHello()
    {
        var store = new InMemoryStateStore(State());
        store.Update(state => state with { AppliedDesiredStateRevision = 1_759_572_000_123 });

        var transport = new FakeNodeTransport();
        var session = new ControlNodeSession(
            transport,
            store,
            new RecordingDispatcher(),
            Options(),
            NullLogger<ControlNodeSession>.Instance);

        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);

        Assert.Equal(1_759_572_000_123, transport.Sent[0].DesiredStateRevision);

        transport.Close();
        await runTask;
    }

    // ------------------------------------------------------------------ 前向兼容

    [Fact]
    public async Task UnknownFrameType_IsIgnoredInsteadOfBreakingTheConnection()
    {
        var (session, transport, _, dispatcher) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame { Type = "future.frame.type" });
        transport.Push(Command(ControlCapabilities.DrawTrigger, DateTimeOffset.UtcNow.AddMinutes(1)));
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Ack);

        Assert.Single(dispatcher.Invocations);
        Assert.False(runTask.IsCompleted);

        transport.Close();
        await runTask;
    }

    [Fact]
    public async Task PostHandshakeErrorFrame_DoesNotEndTheSession()
    {
        var (session, transport, _, _) = CreateSession();
        var runTask = RunAsync(session, transport);
        await transport.WaitForSentAsync(frame => frame.Type == ControlFrameTypes.Hello);
        transport.Push(HelloAck());

        transport.Push(new ControlFrame { Type = ControlFrameTypes.Error, Code = "rate_limited" });
        await Task.Delay(100);

        Assert.False(runTask.IsCompleted);

        transport.Close();
        await runTask;
    }

    // ------------------------------------------------------------------ 测试替身

    private sealed class FakeNodeTransport : IControlNodeTransport
    {
        private readonly Channel<ControlFrame?> _inbound = Channel.CreateUnbounded<ControlFrame?>();
        private readonly object _gate = new();
        private readonly List<ControlFrame> _sent = [];

        public IReadOnlyList<ControlFrame> Sent
        {
            get
            {
                lock (_gate)
                {
                    return _sent.ToList();
                }
            }
        }

        public IReadOnlyList<ControlFrame> SentOfType(string type) =>
            Sent.Where(frame => frame.Type == type).ToList();

        public string? CloseReason { get; private set; }

        public Task SendAsync(ControlFrame frame, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _sent.Add(frame);
            }

            return Task.CompletedTask;
        }

        public async Task<ControlFrame?> ReceiveAsync(CancellationToken cancellationToken) =>
            await _inbound.Reader.ReadAsync(cancellationToken);

        public void Push(ControlFrame frame) => _inbound.Writer.TryWrite(frame);

        public void Close(string? reason = null)
        {
            CloseReason = reason;
            _inbound.Writer.TryWrite(null);
        }

        public ValueTask DisposeAsync()
        {
            _inbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        public async Task WaitForSentAsync(Func<ControlFrame, bool> predicate, TimeSpan? timeout = null)
        {
            var deadline = DateTime.UtcNow + (timeout ?? Timeout);
            while (DateTime.UtcNow < deadline)
            {
                if (Sent.Any(predicate))
                    return;

                await Task.Delay(10);
            }

            throw new TimeoutException("等待发送帧超时。");
        }
    }

    private sealed class InMemoryStateStore(ControlNodeState initial) : IControlNodeStateStore
    {
        private readonly object _gate = new();
        private ControlNodeState _current = initial;

        public ControlNodeState Current
        {
            get
            {
                lock (_gate)
                {
                    return _current;
                }
            }
        }

        public int SaveCount { get; private set; }

        public event EventHandler<ControlNodeState>? Changed;

        public void Update(Func<ControlNodeState, ControlNodeState> mutate)
        {
            ControlNodeState updated;
            lock (_gate)
            {
                updated = mutate(_current);
                if (updated == _current)
                    return;

                _current = updated;
                SaveCount++;
            }

            Changed?.Invoke(this, updated);
        }

        public async Task WaitForRevisionAsync(long revision)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (Current.AppliedDesiredStateRevision == revision)
                    return;

                await Task.Delay(10);
            }

            throw new TimeoutException($"期望状态 revision {revision} 未被应用。");
        }
    }

    private sealed class RecordingDispatcher : IControlCommandDispatcher
    {
        private readonly List<ControlCommandInvocation> _invocations = [];

        public IReadOnlyList<ControlCommandInvocation> Invocations
        {
            get
            {
                lock (_invocations)
                {
                    return _invocations.ToList();
                }
            }
        }

        public Func<ControlCommandInvocation, ControlCommandOutcome>? Handler { get; set; }

        public IReadOnlyList<string> DeclaredCapabilities { get; } =
        [
            ControlCapabilities.StatusRead,
            ControlCapabilities.DrawLock,
            ControlCapabilities.DrawTrigger
        ];

        public bool CanExecute(string capability) =>
            capability is ControlCapabilities.StatusRead or ControlCapabilities.DrawTrigger;

        public Task<ControlCommandOutcome> ExecuteAsync(
            ControlCommandInvocation invocation,
            CancellationToken cancellationToken)
        {
            lock (_invocations)
            {
                _invocations.Add(invocation);
            }

            return Task.FromResult(Handler?.Invoke(invocation) ?? ControlCommandOutcome.Success);
        }
    }
}
