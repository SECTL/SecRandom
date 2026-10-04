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
///         因此 <c>media.play</c> / <c>roster.*</c> / <c>settings.write</c> / <c>proof.list</c>
///         这些还没实现的能力**不出现在清单里**——未声明的能力服务端不会下发。
///     </para>
///     <para>
///         <c>node.restart</c> 已被服务端主动否决（不在授权表里），客户端也**不得实现**：
///         重启教室机等于打断正在上的课，且远程无法恢复。
///     </para>
/// </remarks>
public sealed class ControlCommandDispatcher(
    IControlDrawGate drawGate,
    ILogger<ControlCommandDispatcher> logger) : IControlCommandDispatcher
{
    public IReadOnlyList<string> DeclaredCapabilities { get; } =
    [
        ControlCapabilities.StatusRead,
        ControlCapabilities.DrawLock,
        ControlCapabilities.DrawTrigger
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
        _ => false
    };

    public async Task<ControlCommandOutcome> ExecuteAsync(
        ControlCommandInvocation invocation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        return invocation.Capability switch
        {
            // 状态读取由心跳持续上报（版本/在线/当前班级），命令形式只需确认收到即可。
            ControlCapabilities.StatusRead => ControlCommandOutcome.Success,
            ControlCapabilities.DrawTrigger => await TriggerQuickDrawAsync(cancellationToken).ConfigureAwait(false),
            _ => ControlCommandOutcome.Failure(ControlRejectReasons.CapabilityUnsupported)
        };
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

