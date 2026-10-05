using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Services.Linkage;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>一次远程抽取在设备侧的结局（执行器只报结果，不组装协议回执）。</summary>
/// <param name="Status">结局类别。</param>
/// <param name="Reason">拒绝原因码（控制台直接展示）。</param>
/// <param name="Cause">拒绝的细因，进回执 detail；例如 <c>draw_denied</c> 到底是"要输密码"还是"上课时间"。</param>
/// <param name="Drawn">抽到的成员；只有 <see cref="RemoteDrawStatus.Drawn" /> 时非空。</param>
/// <param name="ListName">实际抽取的名单名（点名抽取才有）。</param>
public sealed record RemoteDrawOutcome(
    RemoteDrawStatus Status,
    string? Reason = null,
    string? Cause = null,
    IReadOnlyList<ControlDrawnMember>? Drawn = null,
    string? ListName = null)
{
    public static RemoteDrawOutcome DrawnFrom(IEnumerable<ControlDrawnMember> drawn, string? listName) =>
        new(RemoteDrawStatus.Drawn, null, null, [.. drawn], listName);

    /// <summary>条件或名单不成立：原因码里已经写明哪个字段、为什么。</summary>
    public static RemoteDrawOutcome Invalid(string reason) => new(RemoteDrawStatus.Unavailable, reason);

    public static RemoteDrawOutcome Busy() => new(RemoteDrawStatus.Busy, "busy");

    /// <summary>本机不允许这次抽取（需要当场输密码、或联动判定不该抽）。</summary>
    public static RemoteDrawOutcome Denied(string cause) => new(RemoteDrawStatus.Unavailable, "draw_denied", cause);

    public static RemoteDrawOutcome Locked() => new(RemoteDrawStatus.Unavailable, "draw_locked");
}

public enum RemoteDrawStatus
{
    Drawn,

    /// <summary>本机明确拒绝（条件不成立、需要本机验证、被锁定）。</summary>
    Unavailable,

    Busy
}

/// <summary>执行完一次远程抽取后的结局。</summary>
public sealed record ControlDrawExecution(bool Succeeded, string? Reason, object? Detail)
{
    public static ControlDrawExecution Success(object detail) => new(true, null, detail);

    public static ControlDrawExecution Failure(string reason, object? detail = null) => new(false, reason, detail);
}

/// <summary>
///     真正去抽的那一步。
/// </summary>
/// <remarks>
///     与校验分开，是为了让"载荷怎么解析、闸门怎么判、回执怎么拼"这三件事能在没有界面的情况下单测：
///     真正要碰界面的只有这个接口的实现，而它被刻意做薄。
/// </remarks>
public interface IControlDrawExecutor
{
    Task<ControlDrawExecution> DrawQuickAsync(CancellationToken cancellationToken);

    Task<ControlDrawExecution> DrawRollCallAsync(
        ControlDrawTriggerRequest request,
        CancellationToken cancellationToken);

    Task<ControlDrawExecution> DrawLotteryAsync(
        ControlDrawTriggerRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
///     <c>draw.trigger</c>：把"远程让我抽一次"翻译成一次真正的抽取。
/// </summary>
/// <remarks>
///     <para>
///         这里只做三件事：解析载荷、过本机抽取闸门、把执行器的结局翻译成协议回执。
///         回执详情必须带上"抽到了谁"——手机与控制台都要能显示，否则远程抽完没人知道发生了什么，
///         而这个功能本来的用途就是"手机上点一下，教室里抽出来"。
///     </para>
///     <para>
///         <b>闸门在解析之后、执行之前</b>：锁定状态不该因为载荷写错而报"字段非法"，
///         但也不该在读不懂载荷的时候就动手——顺序反了会把"我不允许抽"和"你没写对"混在一起。
///     </para>
/// </remarks>
public sealed class ControlDrawTriggerHandler(
    IControlDrawGate drawGate,
    IControlDrawExecutor executor,
    ILogger<ControlDrawTriggerHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(JsonElement? payload, CancellationToken cancellationToken)
    {
        if (!ControlDrawTriggerRequest.TryParse(payload, out var request, out var reason))
            return ControlCommandOutcome.Failure(reason, ControlDrawExecutionFactory.DescribeInvalid(reason));

        if (drawGate.IsDrawLocked)
            return ControlCommandOutcome.Failure("draw_locked", new { draw_locked = true });

        try
        {
            // 三条目标各有各的会话：快抽没有参数，点名与抽奖各自带着自己的名单/奖池与数量。
            // 用 target 分派而不是"带参数就走点名"，是因为抽奖与点名的候选池、页面和回执目标都不同。
            var execution = request.Target switch
            {
                ControlDrawTriggerRequest.TargetRollCall =>
                    await executor.DrawRollCallAsync(request, cancellationToken).ConfigureAwait(false),
                ControlDrawTriggerRequest.TargetLottery =>
                    await executor.DrawLotteryAsync(request, cancellationToken).ConfigureAwait(false),
                _ => await executor.DrawQuickAsync(cancellationToken).ConfigureAwait(false)
            };

            // 抽取结果放 **detail**（协议里 command.result.detail），不是查询用的 result_payload：
            // 手机与旧控制台都按 detail 读这条回执。
            return execution.Succeeded
                ? new ControlCommandOutcome(true, null, ControlCommandOutcome.ToDetail(execution.Detail))
                : ControlCommandOutcome.Failure(execution.Reason ?? ControlRejectReasons.ExecutionFailed, execution.Detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控远程抽取执行失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }
}

/// <summary>把设备侧的结局翻译成协议回执与详情。</summary>
public static class ControlDrawExecutionFactory
{
    public static ControlDrawExecution From(RemoteDrawOutcome outcome, string target, string? listName)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Status switch
        {
            RemoteDrawStatus.Drawn => ControlDrawExecution.Success(new ControlDrawTriggerDetail(
                target,
                listName,
                outcome.Drawn?.Count ?? 0,
                outcome.Drawn ?? [])),
            RemoteDrawStatus.Busy => ControlDrawExecution.Failure("busy", new { drawing = true }),
            _ => ControlDrawExecution.Failure(
                outcome.Reason ?? ControlRejectReasons.ExecutionFailed,
                DescribeFailure(outcome))
        };
    }

    /// <summary>
    ///     拒绝回执的详情。
    /// </summary>
    /// <remarks>
    ///     原因码本身已经把"哪个字段、为什么"编进去了（协议规定如此，控制台旧版本只认原因码），
    ///     详情再拆成结构化字段，是为了手机端不用切字符串就能显示人话。
    /// </remarks>
    public static object? DescribeFailure(RemoteDrawOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return outcome.Reason switch
        {
            "draw_locked" => new { draw_locked = true },
            "busy" => new { drawing = true },
            "draw_denied" => new { reason = outcome.Cause ?? "local_verification_required" },
            { } reason => DescribeInvalid(reason),
            _ => null
        };
    }

    /// <summary>把 <c>invalid_value:&lt;字段&gt;:&lt;为什么&gt;</c> 拆成 <c>field</c>/<c>why</c>。</summary>
    public static object? DescribeInvalid(string reason)
    {
        if (!reason.StartsWith("invalid_value:", StringComparison.Ordinal))
            return null;

        var rest = reason["invalid_value:".Length..];
        var separator = rest.IndexOf(':');
        return separator < 0
            ? new { field = rest, why = "invalid" }
            : new { field = rest[..separator], why = rest[(separator + 1)..] };
    }
}

/// <summary>
///     把 <see cref="LinkageDrawGate" /> 的判定翻译成抽不成的原因。
/// </summary>
/// <remarks>
///     单独抽出来是为了能单测：这段映射决定了手机屏幕上看到的是"这台机器需要本机验证"
///     还是"现在是上课时间"，而它本身与界面无关。
/// </remarks>
public static class ControlDrawGateRejections
{
    public static RemoteDrawOutcome? From(LinkageDrawGate gate) => gate switch
    {
        LinkageDrawGate.Allowed => null,
        LinkageDrawGate.BlockedByRemoteLock => RemoteDrawOutcome.Locked(),
        LinkageDrawGate.BlockedByClassTime => RemoteDrawOutcome.Denied("blocked_by_class_time"),
        LinkageDrawGate.RequiresLocalVerification => RemoteDrawOutcome.Denied("local_verification_required"),
        _ => RemoteDrawOutcome.Denied("local_verification_required")
    };
}
