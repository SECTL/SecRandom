using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Tests;

/// <summary>
///     v4.1 会话层的协议测试：握手帧、两段回执、过期/本地闸、幂等、期望状态与不可重试关闭码。
/// </summary>
public sealed class ControlNodeProtocolTests
{
    private static readonly NodeDeviceCredential Credential = new("dev-1", "secret-1", "SC-K7Q2M9", "第一实验学校");

    [Fact]
    public async Task 首帧必须是hello且带协议4与设备凭据()
    {
        var (session, transport, run, cts) = await StartAsync();

        try
        {
            var hello = Assert.Single(transport.Sent, frame => frame.Topic == ControlV4Protocol.Topics.Hello);
            Assert.Equal(ControlV4Protocol.FrameTypeRequest, hello.Type);
            Assert.Equal("hello-1", hello.Id);

            var payload = AssertPayload(hello);
            Assert.Equal(4, payload.GetProperty("protocol").GetInt32());
            Assert.Equal("secrandom", payload.GetProperty("platform").GetString());
            Assert.Equal("dev-1", payload.GetProperty("device_id").GetString());
            Assert.Equal("secret-1", payload.GetProperty("secret").GetString());
            // device_name 取本机主机名（不是地点名）：地点名在接入响应里，和这帧无关。
            Assert.Equal("测试机", payload.GetProperty("device_name").GetString());
            Assert.Contains("broadcast", payload.GetProperty("capabilities").EnumerateArray()
                .Select(item => item.GetString()));
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task hello上报设置schema与revision()
    {
        var report = new ControlV4SettingsReport
        {
            Revision = 7,
            Schema = new Dictionary<string, ControlV4SettingSchemaField>(StringComparer.Ordinal)
            {
                ["voice.volume"] = new()
                {
                    Type = "int",
                    Label = "音量",
                    Group = "语音",
                    Default = 80,
                    Min = 0,
                    Max = 100,
                    Step = 1
                },
                ["voice.enable"] = new()
                {
                    Type = "bool",
                    Label = "语音播报",
                    Group = "语音",
                    Default = true
                }
            }
        };

        var (session, transport, run, cts) = await StartAsync(
            settingsReportSource: new FixedSettingsReportSource(report));

        try
        {
            var hello = Assert.Single(transport.Sent, frame => frame.Topic == ControlV4Protocol.Topics.Hello);
            var settings = AssertPayload(hello).GetProperty("settings");

            Assert.Equal(7, settings.GetProperty("revision").GetInt32());
            var schema = settings.GetProperty("schema");
            Assert.Equal("int", schema.GetProperty("voice.volume").GetProperty("type").GetString());
            Assert.Equal(80, schema.GetProperty("voice.volume").GetProperty("default").GetInt32());
            Assert.True(schema.TryGetProperty("voice.enable", out _));
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 应答缺失draw_locked时按锁定上报_并沿用服务端心跳()
    {
        var store = new FakeStateStore();
        var transport = new FakeTransport();
        transport.OnSend = frame =>
        {
            if (frame.Id == "hello-1")
                transport.Push(ResponseJson("hello-1", true, new
                {
                    heartbeat_seconds = 7,
                    offline_after_seconds = 21,
                    desired_state_revision = 9L,
                    device = new { id = "dev-1", name = "讲台机" }
                }));
        };

        var session = CreateSession(transport, store, new FakeDispatcher());
        using var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);

        await WaitUntilAsync(() => session.IsHandshaken);

        // 回执里没有 draw_locked：这是"服务端没说允许抽"，必须按锁定处理。
        Assert.True(store.Current.DrawLocked);
        Assert.Equal(9L, store.Current.AppliedDesiredStateRevision);

        cts.Cancel();
        await run;
    }

    [Theory]
    [InlineData(ControlV4Protocol.ErrorCodes.BadSecret)]
    [InlineData(ControlV4Protocol.ErrorCodes.DeviceRevoked)]
    [InlineData(ControlV4Protocol.ErrorCodes.ProtocolTooOld)]
    [InlineData(ControlV4Protocol.ErrorCodes.NotAuthed)]
    public async Task 不可重试握手错误直接结束会话(string code)
    {
        var transport = new FakeTransport();
        transport.OnSend = frame =>
        {
            if (frame.Id == "hello-1")
                transport.Push(ErrorResponseJson("hello-1", code));
        };

        var session = CreateSession(transport, new FakeStateStore(), new FakeDispatcher());
        var result = await session.RunAsync(CancellationToken.None);

        Assert.Equal(ControlSessionEndReason.Terminal, result.Reason);
        Assert.Equal(code, result.Detail);
    }

    [Fact]
    public async Task 命令先发ack再执行_结果帧的id等于command_id()
    {
        var dispatcher = new FakeDispatcher();
        var (session, transport, run, cts) = await StartAsync(dispatcher: dispatcher);

        try
        {
            transport.Push(CommandJson("cmd-1", ControlCapabilities.SettingsRead, expiresAt: FutureMs()));

            var resultFrame = await transport.WaitForSentAsync(frame => frame.Topic == ControlV4Protocol.Topics.CommandResult);

            Assert.Equal("cmd-1", resultFrame.Id);
            var sentAfterCommand = transport.Sent
                .Where(frame => frame.Id == "cmd-1")
                .Select(frame => frame.Topic)
                .ToArray();
            Assert.Equal(["cmd.ack", "cmd.result"], sentAfterCommand);
            Assert.True(AssertPayload(resultFrame).GetProperty("ok").GetBoolean());
            Assert.Equal(1, dispatcher.Executions);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 过期命令只回ack且不执行()
    {
        var dispatcher = new FakeDispatcher();
        var (session, transport, run, cts) = await StartAsync(dispatcher: dispatcher);

        try
        {
            transport.Push(CommandJson("cmd-exp", ControlCapabilities.SettingsRead, expiresAt: 1_000));

            var ack = await transport.WaitForSentAsync(frame => frame.Topic == ControlV4Protocol.Topics.CommandAck);

            Assert.False(AssertPayload(ack).GetProperty("accepted").GetBoolean());
            Assert.Equal("expired", AssertPayload(ack).GetProperty("code").GetString());
            Assert.Equal(0, dispatcher.Executions);
            Assert.DoesNotContain(transport.Sent, frame => frame.Topic == ControlV4Protocol.Topics.CommandResult);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 本机开关关闭时拒绝命令_不发结果()
    {
        var (session, transport, run, cts) = await StartAsync(remoteEnabled: false);

        try
        {
            transport.Push(CommandJson("cmd-local", ControlCapabilities.SettingsRead, FutureMs()));

            var ack = await transport.WaitForSentAsync(frame => frame.Topic == ControlV4Protocol.Topics.CommandAck);
            Assert.Equal("local_remote_disabled", AssertPayload(ack).GetProperty("code").GetString());
            Assert.DoesNotContain(transport.Sent, frame => frame.Topic == ControlV4Protocol.Topics.CommandResult);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 未声明能力返回capability_unsupported()
    {
        var (session, transport, run, cts) = await StartAsync();

        try
        {
            transport.Push(CommandJson("cmd-unknown", "node.restart", FutureMs()));

            var ack = await transport.WaitForSentAsync(frame => frame.Topic == ControlV4Protocol.Topics.CommandAck);
            Assert.Equal("capability_unsupported", AssertPayload(ack).GetProperty("code").GetString());
            Assert.DoesNotContain(transport.Sent, frame => frame.Topic == ControlV4Protocol.Topics.CommandResult);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 重复command_id重放上一次的结果_不重复执行()
    {
        var dispatcher = new FakeDispatcher();
        var (session, transport, run, cts) = await StartAsync(dispatcher: dispatcher);

        try
        {
            transport.Push(CommandJson("cmd-dup", ControlCapabilities.SettingsRead, FutureMs()));
            await transport.WaitForSentAsync(frame => frame.Topic == ControlV4Protocol.Topics.CommandResult);

            transport.Push(CommandJson("cmd-dup", ControlCapabilities.SettingsRead, FutureMs()));
            await Task.Delay(150);

            Assert.Equal(1, dispatcher.Executions);
            Assert.Equal(2, transport.Sent.Count(frame => frame.Topic == ControlV4Protocol.Topics.CommandResult));
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 期望状态按revision单调应用_缺draw_locked按锁定()
    {
        var store = new FakeStateStore();
        var (session, transport, run, cts) = await StartAsync(store: store);

        try
        {
            transport.Push(DesiredStateJson(revision: 100, "class-1", drawLocked: false));
            await WaitUntilAsync(() => store.Current.AppliedDesiredStateRevision == 100);
            Assert.False(store.Current.DrawLocked);
            Assert.Equal("class-1", store.Current.ClassId);

            // 低 revision 是迟到帧：必须丢掉，否则会把刚解锁的教室又锁回去。
            transport.Push(DesiredStateJson(revision: 99, "class-1", drawLocked: true));
            await Task.Delay(100);
            Assert.False(store.Current.DrawLocked);

            // 缺 draw_locked 的新 revision 按锁定处理（fail-closed）。
            transport.Push(DesiredStateJson(revision: 101, null, drawLocked: null));
            await WaitUntilAsync(() => store.Current.AppliedDesiredStateRevision == 101);
            Assert.True(store.Current.DrawLocked);
            Assert.Null(store.Current.ClassId);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 握手后按已分配班级占班()
    {
        // 接入时就分过班的机器：握手成功必须立刻 class.select，否则服务端占用表里没有它，
        // 网页上这个班看不到客户端、控制台的「允许抽取」也推不下来。
        var store = new FakeStateStore();
        store.Current = store.Current with { ClassId = "class-1" };
        var (_, transport, run, cts) = await StartAsync(store: store);

        try
        {
            var frame = await transport.WaitForSentAsync(
                f => f.Topic == ControlV4Protocol.Topics.ClassSelect);
            var payload = AssertPayload(frame);
            Assert.Equal(ControlV4Protocol.FrameTypeRequest, frame.Type);
            Assert.Equal("class-1", payload.GetProperty("class_id").GetString());
            Assert.Equal("secrandom", payload.GetProperty("platform").GetString());

            transport.Push(ResponseJson(frame.Id, true, new { granted = true, desired_state_revision = 7L }));
            await Task.Delay(50);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    [Fact]
    public async Task 换班时补发占班_清空班级发释放()
    {
        var store = new FakeStateStore();
        var (_, transport, run, cts) = await StartAsync(store: store);

        try
        {
            // 从没分过班：刚连上不该凭空发一帧 class.select。
            await Task.Delay(100);
            Assert.DoesNotContain(transport.Sent, f => f.Topic == ControlV4Protocol.Topics.ClassSelect);

            store.Update(state => state with { ClassId = "class-1" });
            var select = await transport.WaitForSentAsync(
                f => f.Topic == ControlV4Protocol.Topics.ClassSelect);
            Assert.Equal("class-1", AssertPayload(select).GetProperty("class_id").GetString());
            transport.Push(ResponseJson(select.Id, true, new { granted = true }));

            // 清空班级 = 下课 = class.release（不带 class_id，服务端从占用记录反推）。
            store.Update(state => state with { ClassId = null });
            var release = await transport.WaitForSentAsync(
                f => f.Topic == ControlV4Protocol.Topics.ClassRelease);
            Assert.False(AssertPayload(release).TryGetProperty("class_id", out _));

            transport.Push(ResponseJson(release.Id, true, new { released = true }));
            await Task.Delay(50);
        }
        finally
        {
            await StopAsync(cts, run);
        }
    }

    private static ControlNodeSession CreateSession(
        FakeTransport transport,
        FakeStateStore store,
        FakeDispatcher dispatcher,
        IControlSettingsReportSource? settingsReportSource = null)
    {
        var options = new ControlNodeClientOptions { Version = "1.0.0", HostName = "测试机" };
        return new ControlNodeSession(
            transport, store, dispatcher, Credential, options,
            NullLogger<ControlNodeSession>.Instance,
            settingsReportSource: settingsReportSource);
    }

    private static async Task<(ControlNodeSession Session, FakeTransport Transport, Task<ControlSessionResult> Run, CancellationTokenSource Cts)>
        StartAsync(
            FakeDispatcher? dispatcher = null,
            bool remoteEnabled = true,
            FakeStateStore? store = null,
            IControlSettingsReportSource? settingsReportSource = null)
    {
        var transport = new FakeTransport();
        transport.OnSend = frame =>
        {
            if (frame.Id == "hello-1")
                transport.Push(ResponseJson("hello-1", true, new
                {
                    heartbeat_seconds = 3600,
                    offline_after_seconds = 10800,
                    draw_locked = true,
                    desired_state_revision = 0L,
                    device = new { id = "dev-1", name = "测试机" }
                }));
        };

        var effectiveStore = store ?? new FakeStateStore();
        effectiveStore.Current = effectiveStore.Current with { RemoteControlEnabled = remoteEnabled };
        var session = CreateSession(transport, effectiveStore, dispatcher ?? new FakeDispatcher(), settingsReportSource);
        var cts = new CancellationTokenSource();
        var run = session.RunAsync(cts.Token);

        await WaitUntilAsync(() => session.IsHandshaken);
        return (session, transport, run, cts);
    }

    private static async Task StopAsync(CancellationTokenSource cts, Task run)
    {
        cts.Cancel();
        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!predicate())
        {
            if (Environment.TickCount64 > deadline)
                throw new TimeoutException("等待条件超时");
            await Task.Delay(10);
        }
    }

    private sealed class FixedSettingsReportSource(ControlV4SettingsReport report) : IControlSettingsReportSource
    {
        public ControlV4SettingsReport? Build() => report;
    }

    private static JsonElement AssertPayload(ControlV4Frame frame)
    {
        Assert.NotNull(frame.Payload);
        return frame.Payload!.Value;
    }

    private static long FutureMs() => DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds();

    private static string CommandJson(string commandId, string cmd, long expiresAt) =>
        ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeEvent,
            Topic = ControlV4Protocol.Topics.Command,
            Payload = ControlV4Json.ToElement(new
            {
                command_id = commandId,
                cmd,
                args = new { },
                issuer = "user-1",
                expires_at = expiresAt
            })
        });

    private static string DesiredStateJson(long revision, string? classId, bool? drawLocked) =>
        ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeEvent,
            Topic = ControlV4Protocol.Topics.DesiredState,
            Payload = ControlV4Json.ToElement(new
            {
                revision,
                class_id = classId,
                platform = "secrandom",
                draw_locked = drawLocked
            })
        });

    private static string ResponseJson(string id, bool ok, object payload) =>
        ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeResponse,
            Id = id,
            Ok = ok,
            Payload = ControlV4Json.ToElement(payload)
        });

    private static string ErrorResponseJson(string id, string code) =>
        ControlV4Json.Serialize(new ControlV4Frame
        {
            Type = ControlV4Protocol.FrameTypeResponse,
            Id = id,
            Ok = false,
            Code = code
        });

    private sealed class FakeDispatcher : IControlCommandDispatcher
    {
        private int _executions;

        public int Executions => _executions;

        public IReadOnlyList<string> DeclaredCapabilities { get; } =
        [
            ControlCapabilities.Broadcast,
            ControlCapabilities.SettingsRead,
            ControlCapabilities.DrawTrigger
        ];

        public bool CanExecute(string capability) => DeclaredCapabilities.Contains(capability);

        public Task<ControlCommandOutcome> ExecuteAsync(
            ControlCommandInvocation invocation,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _executions);
            return Task.FromResult(ControlCommandOutcome.SuccessWith(new { ok = true }));
        }
    }

    private sealed class FakeStateStore : IControlNodeStateStore
    {
        public ControlNodeState Current { get; set; } = new()
        {
            NodeId = "dev-1",
            RemoteControlEnabled = true,
            ServerUrl = ControlNodeClientOptions.DefaultEndpoint
        };

        public event EventHandler<ControlNodeState>? Changed;
        public event EventHandler<ControlNodeState>? StateChanged
        {
            add => Changed += value;
            remove => Changed -= value;
        }

        public void Update(Func<ControlNodeState, ControlNodeState> update)
        {
            Current = update(Current);
            Changed?.Invoke(this, Current);
        }

        public void RaiseForTest() => Changed?.Invoke(this, Current);
    }

    private sealed class FakeTransport : IControlNodeTransport
    {
        private readonly Channel<string?> _inbound = Channel.CreateUnbounded<string?>();
        private readonly List<ControlV4Frame> _sent = [];

        public Action<ControlV4Frame>? OnSend { get; set; }

        public IReadOnlyList<ControlV4Frame> Sent => _sent;

        public string? CloseReason => null;

        public int? CloseCode => null;

        public Task SendAsync(string frameJson, CancellationToken cancellationToken)
        {
            var frame = ControlV4Json.TryParse(frameJson);
            if (frame is null)
                throw new InvalidOperationException($"坏帧：{frameJson}");

            _sent.Add(frame);
            OnSend?.Invoke(frame);
            return Task.CompletedTask;
        }

        public async Task<string?> ReceiveAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await _inbound.Reader.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }

        public void Push(string frameJson) => _inbound.Writer.TryWrite(frameJson);

        public async Task<ControlV4Frame> WaitForSentAsync(
            Func<ControlV4Frame, bool> predicate,
            int timeoutMs = 3000)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                var match = _sent.FirstOrDefault(predicate);
                if (match is not null)
                    return match;

                if (Environment.TickCount64 > deadline)
                    throw new TimeoutException($"等待发送帧超时，已发送：{string.Join(",", _sent.Select(f => f.Topic))}");

                await Task.Delay(10);
            }
        }

        public ValueTask DisposeAsync()
        {
            _inbound.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}