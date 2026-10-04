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
///         <c>roster.write</c>（名单下发）。
///     </para>
///     <para>
///         **读类能力（<c>roster.read</c> / <c>proof.list</c>）不声明**：服务端目前只接受
///         <c>action</c> 与 <c>set_desired_state</c> 两种 kind，没有查询通道，声明了也永远收不到命令。
///     </para>
///     <para>
///         <c>node.restart</c> 已被服务端主动否决（不在授权表里），客户端也**不得实现**：
///         重启教室机等于打断正在上的课，且远程无法恢复。
///     </para>
/// </remarks>
public sealed class ControlCommandDispatcher(
    IControlDrawGate drawGate,
    ControlMediaPlayHandler mediaPlay,
    ControlSettingsPatchHandler settingsPatch,
    ControlRosterPushHandler rosterPush,
    ILogger<ControlCommandDispatcher> logger) : IControlCommandDispatcher
{
    public IReadOnlyList<string> DeclaredCapabilities { get; } =
    [
        ControlCapabilities.StatusRead,
        ControlCapabilities.DrawLock,
        ControlCapabilities.DrawTrigger,
        ControlCapabilities.MediaPlay,
        ControlCapabilities.SettingsWrite,
        ControlCapabilities.RosterWrite
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
        ControlCapabilities.MediaPlay => true,
        ControlCapabilities.SettingsWrite => true,
        ControlCapabilities.RosterWrite => true,
        _ => false
    };

    public async Task<ControlCommandOutcome> ExecuteAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // 改设置与换名单在抽取进行中一律拒绝：这两件事都会改到"正在抽的那一轮"赖以成立的配置
        // 与候选人，中途生效会让结果与证明对不上。抽取本身（draw.trigger）自己会拒绝重入。
        if (invocation.Capability is ControlCapabilities.SettingsWrite or ControlCapabilities.RosterWrite
            && await IsDrawInProgressAsync().ConfigureAwait(false))
        {
            return ControlCommandOutcome.Failure("busy");
        }

        return invocation.Capability switch
        {
            // 状态读取由心跳持续上报（版本/在线/当前名单摘要），命令形式只需确认收到即可。
            ControlCapabilities.StatusRead => ControlCommandOutcome.Success,
            ControlCapabilities.DrawTrigger => await TriggerQuickDrawAsync(cancellationToken).ConfigureAwait(false),
            ControlCapabilities.MediaPlay =>
                await mediaPlay.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.SettingsWrite =>
                await settingsPatch.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            ControlCapabilities.RosterWrite =>
                await rosterPush.ExecuteAsync(invocation.Payload, cancellationToken).ConfigureAwait(false),
            _ => ControlCommandOutcome.Failure(ControlRejectReasons.CapabilityUnsupported)
        };
    }

    /// <summary>本机是否正在抽取。读的是页面 ViewModel 的真实状态，而不是猜测。</summary>
    private static async Task<bool> IsDrawInProgressAsync()
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => IAppHost.GetService<QuickDrawPageViewModel>().IsDrawing);
        }
        catch (Exception)
        {
            // 取不到状态时不阻断命令：宁可让命令继续，也不要因为读不到界面状态就永久拒绝远程操作。
            return false;
        }
    }

    private async Task<ControlCommandOutcome> TriggerQuickDrawAsync(CancellationToken cancellationToken)
    {
        if (drawGate.IsDrawLocked)
            return ControlCommandOutcome.Failure("draw_locked");

        try
        {
            var drawn = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                // 页面 ViewModel 在这里才解析：Host 启动是在线程池线程上构造托管服务的，
                // 在构造函数里创建界面对象会把它绑到错误的线程上。
                var quickDraw = IAppHost.GetService<QuickDrawPageViewModel>();

                // 让课堂看到滚动动画与结果：远程抽取不能悄悄发生。
                App.ShowQuickDrawWindow();
                return await quickDraw.StartRemoteDrawAsync(cancellationToken);
            });

            return drawn
                ? ControlCommandOutcome.Success
                : ControlCommandOutcome.Failure("draw_denied");
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

