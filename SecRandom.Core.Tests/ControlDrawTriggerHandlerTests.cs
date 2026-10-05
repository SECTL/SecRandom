using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.ControlNode;
using SecRandom.Services.Linkage;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Tests;

/// <summary>
///     <c>draw.trigger</c> 落到设备上的那一半：解析后的路由、闸门顺序与回执形状。
/// </summary>
/// <remarks>
///     <para>
///         真正动手抽的那一步要回到 UI 线程（<c>ControlPageDrawExecutor</c>），单测进程里没有界面，
///         因此这里替换掉 <see cref="IControlDrawExecutor" />，钉住它<strong>之外</strong>的全部判断：
///         不带载荷必须走快抽（旧控制台的回归点）、<c>target</c> 决定调哪条路径、
///         锁定/忙碌/条件不成立分别回什么原因码与 detail。
///     </para>
///     <para>
///         拒绝原因码是控制台与手机直接展示的东西，所以断言到字符串级别，而不是"返回了 false"。
///     </para>
/// </remarks>
public sealed class ControlDrawTriggerHandlerTests
{
    private static JsonElement Payload(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public async Task 不带载荷仍然走快抽这条老路径()
    {
        // 向后兼容的回归点：部署在教室里的旧控制台发的就是不带 payload 的 draw.trigger。
        var executor = new RecordingExecutor();

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(null, CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Reason);
        Assert.True(executor.QuickCalls > 0);
        Assert.Null(executor.LastRollCallRequest);
    }

    [Fact]
    public async Task 点名载荷把解析结果交给点名执行器()
    {
        var executor = new RecordingExecutor();

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(
            Payload("""{ "target": "roll_call", "list_name": "高一（1）班", "count": 2, "gender": "男" }"""),
            CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Reason);
        Assert.Equal(0, executor.QuickCalls);
        Assert.NotNull(executor.LastRollCallRequest);
        Assert.Equal("高一（1）班", executor.LastRollCallRequest!.ListName);
        Assert.Equal(2, executor.LastRollCallRequest.Count);
        Assert.Equal("男", executor.LastRollCallRequest.Gender);
    }

    [Fact]
    public async Task 成功回执带上目标名单与抽到的人()
    {
        var executor = new RecordingExecutor
        {
            RollCall = RemoteDrawOutcome.DrawnFrom(
                [new ControlDrawnMember("01", "张三"), new ControlDrawnMember(null, "李四")],
                "高一（1）班")
        };

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(
            Payload("""{ "target": "roll_call", "list_name": "高一（1）班", "count": 2 }"""),
            CancellationToken.None);

        Assert.True(outcome.Ok, outcome.Reason);
        var detail = outcome.Detail!.Value;
        Assert.Equal("roll_call", detail.GetProperty("target").GetString());
        Assert.Equal("高一（1）班", detail.GetProperty("list_name").GetString());
        Assert.Equal(2, detail.GetProperty("count").GetInt32());

        var drawn = detail.GetProperty("drawn");
        Assert.Equal(2, drawn.GetArrayLength());
        Assert.Equal("01", drawn[0].GetProperty("id").GetString());
        Assert.Equal("张三", drawn[0].GetProperty("name").GetString());
        // 协议按 WhenWritingNull 序列化：没有学号/姓名时字段**不出现**（而不是空串），手机端按缺省处理。
        Assert.False(drawn[1].TryGetProperty("id", out _));
        Assert.Equal("李四", drawn[1].GetProperty("name").GetString());
    }

    [Fact]
    public async Task 本机锁定抽取时直接拒绝且不执行()
    {
        var executor = new RecordingExecutor();

        var outcome = await CreateHandler(executor, locked: true).ExecuteAsync(
            Payload("""{ "target": "roll_call" }"""),
            CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("draw_locked", outcome.Reason);
        Assert.True(outcome.Detail!.Value.GetProperty("draw_locked").GetBoolean());
        Assert.Equal(0, executor.QuickCalls);
        Assert.Null(executor.LastRollCallRequest);
    }

    [Fact]
    public async Task 正在抽取时回busy并说明设备在忙()
    {
        var executor = new RecordingExecutor { RollCall = RemoteDrawOutcome.Busy() };

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(
            Payload("""{ "target": "roll_call" }"""),
            CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("busy", outcome.Reason);
        Assert.True(outcome.Detail!.Value.GetProperty("drawing").GetBoolean());
    }

    [Fact]
    public async Task 条件不成立时原因码原样回给控制台并拆出字段()
    {
        var executor = new RecordingExecutor
        {
            RollCall = RemoteDrawOutcome.Invalid("invalid_value:gender:not_in_list")
        };

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(
            Payload("""{ "target": "roll_call", "gender": "女" }"""),
            CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("invalid_value:gender:not_in_list", outcome.Reason);
        Assert.Equal("gender", outcome.Detail!.Value.GetProperty("field").GetString());
        Assert.Equal("not_in_list", outcome.Detail!.Value.GetProperty("why").GetString());
    }

    [Fact]
    public async Task 载荷写错时在动手之前就被挡住()
    {
        var executor = new RecordingExecutor();

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(
            Payload("""{ "target": "lottery" }"""),
            CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("invalid_value:target:unsupported", outcome.Reason);
        Assert.Equal("target", outcome.Detail!.Value.GetProperty("field").GetString());
        Assert.Equal(0, executor.QuickCalls);
        Assert.Null(executor.LastRollCallRequest);
    }

    [Fact]
    public async Task 执行器抛异常时回执行失败而不是崩掉节点()
    {
        var executor = new RecordingExecutor { ThrowOnQuick = true };

        var outcome = await CreateHandler(executor, locked: false).ExecuteAsync(null, CancellationToken.None);

        Assert.False(outcome.Ok);
        Assert.Equal("execution_failed", outcome.Reason);
    }

    [Theory]
    [InlineData(LinkageDrawGate.Allowed, null)]
    [InlineData(LinkageDrawGate.BlockedByRemoteLock, "draw_locked")]
    [InlineData(LinkageDrawGate.BlockedByClassTime, "draw_denied")]
    [InlineData(LinkageDrawGate.RequiresLocalVerification, "draw_denied")]
    public void 闸门判定映射成控制台能区分的原因(LinkageDrawGate gate, string? expectedReason)
    {
        var rejection = ControlDrawGateRejections.From(gate);

        if (expectedReason is null)
        {
            Assert.Null(rejection);
            return;
        }

        Assert.NotNull(rejection);
        Assert.Equal(expectedReason, rejection!.Reason);
    }

    [Fact]
    public void 上课时间与需要本机验证给不同的细因()
    {
        var classTime = ControlDrawExecutionFactory.From(
            ControlDrawGateRejections.From(LinkageDrawGate.BlockedByClassTime)!, "roll_call", null);
        var verification = ControlDrawExecutionFactory.From(
            ControlDrawGateRejections.From(LinkageDrawGate.RequiresLocalVerification)!, "roll_call", null);

        Assert.Equal("blocked_by_class_time", SerializeDetail(classTime).GetProperty("reason").GetString());
        Assert.Equal("local_verification_required", SerializeDetail(verification).GetProperty("reason").GetString());
    }

    /// <summary>回执 detail 在协议里就是 JSON：断言之前先按线格式序列化，避免断言一个只有内部形状的对象。</summary>
    private static JsonElement SerializeDetail(ControlDrawExecution execution) =>
        JsonSerializer.SerializeToElement(execution.Detail, ControlProtocolJson.Options);

    private static ControlDrawTriggerHandler CreateHandler(RecordingExecutor executor, bool locked) =>
        new(new StubDrawGate(locked), executor, NullLogger<ControlDrawTriggerHandler>.Instance);

    private sealed class StubDrawGate(bool locked) : IControlDrawGate
    {
        public bool IsDrawLocked => locked;
    }

    private sealed class RecordingExecutor : IControlDrawExecutor
    {
        public int QuickCalls { get; private set; }

        public ControlDrawTriggerRequest? LastRollCallRequest { get; private set; }

        public RemoteDrawOutcome Quick { get; init; } =
            RemoteDrawOutcome.DrawnFrom([new ControlDrawnMember("01", "张三")], "快抽默认名单");

        public RemoteDrawOutcome RollCall { get; init; } =
            RemoteDrawOutcome.DrawnFrom([new ControlDrawnMember("01", "张三")], "高一（1）班");

        public bool ThrowOnQuick { get; init; }

        public Task<ControlDrawExecution> DrawQuickAsync(CancellationToken cancellationToken)
        {
            QuickCalls++;
            if (ThrowOnQuick)
                throw new InvalidOperationException("boom");

            return Task.FromResult(ControlDrawExecutionFactory.From(Quick, ControlDrawTriggerRequest.TargetQuick, Quick.ListName));
        }

        public Task<ControlDrawExecution> DrawRollCallAsync(
            ControlDrawTriggerRequest request,
            CancellationToken cancellationToken)
        {
            LastRollCallRequest = request;
            return Task.FromResult(ControlDrawExecutionFactory.From(RollCall, ControlDrawTriggerRequest.TargetRollCall, RollCall.ListName));
        }
    }
}
