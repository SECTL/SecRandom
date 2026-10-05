using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     集控节点能执行的能力。
/// </summary>
/// <remarks>
///     <para>
///         <b>只声明真的实现了的能力。</b>能力清单是节点对服务端的承诺：声明了却执行不了，
///         服务端就会下发注定失败的命令，控制台看到的是一条"设备已接受但失败"的运行故障。
///     </para>
///     <para>
///         已经实现并声明的：<c>node.status.read</c>（心跳上报）、<c>draw.lock</c>（期望状态）、
///         <c>draw.trigger</c>、<c>media.play</c>（语音播报）、<c>settings.write</c>（白名单设置）、
///         <c>roster.write</c>（名单下发，点名叫 students、抽奖靠 <c>roster_kind</c>+<c>prizes</c>）、
///         <c>roster.read</c>（读名单）与 <c>settings.read</c>（读设置目录）。
///     </para>
///     <para>
///         两条读能力走的是**查询**（服务端下发 <c>kind: "query"</c>，答案在
///         <c>command.result.result_payload</c> 里回来）。它们真的会到达——早先写过
///         "服务端没有查询通道、声明了也收不到"，那是查询通道上线之前的旧结论，
///         留着它会让下一个人把能用的功能当成死代码。
///     </para>
///     <para>
///         <c>proof.list</c> 仍未实现，因此不声明：声明一个执行不了的能力，
///         控制台看到的会是一条"设备已接受但失败"的运行故障。
///     </para>
///     <para>
///         <c>node.restart</c> 已被服务端主动否决（不在授权表里），客户端也**不得实现**：
///         重启教室机等于打断正在上的课，且远程无法恢复。
///     </para>
///     <para>
///         <c>draw.trigger</c> 现在**带参数**：<c>target</c>/<c>list_name</c>/<c>count</c>/<c>gender</c>/<c>group</c>。
///         不带载荷仍然是"按快抽默认名单抽一次"（旧控制台按钮的行为，必须保持），
///         带 <c>target=roll_call</c> 则用点名会话在指定名单与条件下抽，并把结果留在教室机屏幕上。
///         载荷解析与条件判定在 Core 的 <c>ControlDrawTriggerRequest</c> / <c>ControlDrawConditions</c> 里，
///         落地执行在 <c>ControlDrawTriggerHandler</c> + <c>ControlPageDrawExecutor</c>。
///     </para>
/// </remarks>
public sealed class ControlCommandDispatcher(
    ControlDrawTriggerHandler drawTrigger,
    ControlDrawResetHandler drawReset,
    ControlMediaPlayHandler mediaPlay,
    ControlSettingsPatchHandler settingsPatch,
    ControlRosterPushHandler rosterPush,
    ControlRosterReadHandler rosterRead,
    ControlSettingsReadHandler settingsRead) : IControlCommandDispatcher
{
    public IReadOnlyList<string> DeclaredCapabilities { get; } =
    [
        ControlCapabilities.StatusRead,
        ControlCapabilities.DrawLock,
        ControlCapabilities.DrawTrigger,
        ControlCapabilities.DrawReset,
        ControlCapabilities.MediaPlay,
        ControlCapabilities.SettingsWrite,
        ControlCapabilities.RosterWrite,
        ControlCapabilities.RosterRead,
        ControlCapabilities.SettingsRead
    ];

    /// <summary>
    ///     能否把某个能力当作**动作命令**执行。
    /// </summary>
    /// <remarks>
    ///     <c>draw.lock</c> 是**期望状态**：服务端通过 <c>desired_state</c> 的 <c>revision</c> 单调收敛它，
    ///     一条不带 revision 的动作命令既不能让状态收敛，还会让"服务端认为的状态"与"设备实际状态"
    ///     分叉（此后服务端补投同一个 revision 会被本机按 §7 丢弃）。所以声明它、但拒绝它的动作形式。
    /// </remarks>
    public bool CanExecute(string capability) => capability switch
    {
        ControlCapabilities.StatusRead => true,
        ControlCapabilities.DrawTrigger => true,
        ControlCapabilities.DrawReset => true,
        ControlCapabilities.MediaPlay => true,
        ControlCapabilities.SettingsWrite => true,
        ControlCapabilities.RosterWrite => true,
        ControlCapabilities.RosterRead => true,
        ControlCapabilities.SettingsRead => true,
        _ => false
    };

    public async Task<ControlCommandOutcome> ExecuteAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // 改设置、换名单与重置在抽取进行中一律拒绝（策略见 ControlDrawBusyGuard）：
        // 重置尤其不能边抽边清——那一轮的进度会被抹掉，结果与临时记录就对不上了。
        // 抽取本身（draw.trigger）自己会拒绝重入。
        if (ControlDrawBusyGuard.IsRefusedWhileDrawing(invocation.Capability)
            && await IsDrawInProgressAsync().ConfigureAwait(false))
        {
            return ControlCommandOutcome.Failure("busy", new { drawing = true });
        }

        return invocation.Capability switch
        {
            // 状态读取由心跳持续上报（版本/在线/当前名单摘要），命令形式只需确认收到即可。
            ControlCapabilities.StatusRead => ControlCommandOutcome.Success,
            ControlCapabilities.DrawTrigger =>
                await drawTrigger.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.DrawReset =>
                await drawReset.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.MediaPlay =>
                await mediaPlay.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SettingsWrite =>
                await settingsPatch.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RosterWrite =>
                await rosterPush.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RosterRead =>
                await rosterRead.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SettingsRead =>
                await settingsRead.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            _ => ControlCommandOutcome.Failure(
                ControlRejectReasons.CapabilityUnsupported,
                new { capability = invocation.Capability, supported = DeclaredCapabilities })
        };
    }

    /// <summary>本机是否正在抽取。读的是页面 ViewModel 的真实状态，而不是猜测。</summary>
    /// <remarks>
    ///     三个抽取页都要问：点名声明的忙碌状态只覆盖点名页，抽奖页抽到一半时同样不能被改设置或清进度——
    ///     <c>draw.reset</c> 尤其不能边抽边清，那一轮的进度会被抹掉，结果与临时记录就对不上了。
    /// </remarks>
    private static async Task<bool> IsDrawInProgressAsync()
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => IAppHost.GetService<QuickDrawPageViewModel>().IsDrawing
                      || IAppHost.GetService<RollCallPageViewModel>().IsDrawing
                      || IAppHost.GetService<LotteryPageViewModel>().IsDrawing);
        }
        catch (Exception)
        {
            // 取不到状态时不阻断命令：宁可让命令继续，也不要因为读不到界面状态就永久拒绝远程操作。
            return false;
        }
    }
}

