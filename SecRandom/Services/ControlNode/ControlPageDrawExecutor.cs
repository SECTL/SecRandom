using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.ControlNode;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     远程点名把主窗口"送到最前面"时该做哪几件事。
/// </summary>
/// <remarks>
///     <para>
///         <b>为什么这条路径可以主动抢前台</b>：远程点名的结果必须让教室看见——老师按下按钮，
///         屏幕上就要出现抽到了谁。普通显示与浮窗仍遵守用户的"不抢焦点/置顶模式"设置，
///         只有这一条明确的"必须被看到"的路径例外，且例外只限<strong>还原、显示、激活</strong>三件事。
///     </para>
///     <para>
///         <b>为什么不闪一下 Topmost</b>：那是绕过 Windows 前台锁的老办法，但它会与用户的置顶模式
///         （尤其 UIAccess：那条路走的是提权 token 与重启，置顶由平台层管理）打架，
///         恢复失败还会把窗口永久钉在最前面。这里选"还原 + 显示 + 激活"：
///         窗口本就配置成置顶时它已经在最前，未置顶时激活也会把它带到前台。
///         代价是当别的进程正持有前台时（用户正在别处打字），Windows 可能只闪任务栏——
///         那种情况下强行抢焦会打断别人正在做的事，不该由一条远程命令单方面决定。
///     </para>
/// </remarks>
/// <param name="Restore">窗口最小化时需要先还原（最小化的窗口无法被有意义地激活）。</param>
/// <param name="Show">窗口当前不可见时需要显示。</param>
/// <param name="Activate">是否激活到前台（这条路径上永远为真）。</param>
public readonly record struct RemoteDrawWindowPlan(bool Restore, bool Show, bool Activate)
{
    public static RemoteDrawWindowPlan Resolve(bool isVisible, bool isMinimized) =>
        new(Restore: isMinimized, Show: !isVisible, Activate: true);
}

/// <summary>远程抽取的结果要落在哪种宿主上。</summary>
public enum RemoteDrawSurface
{
    /// <summary>桌面：有独立主窗口，可以还原 / 显示 / 激活。</summary>
    DesktopWindow,

    /// <summary>
    ///     单窗口宿主（平板）：主界面本身就是宿主里的当前页，没有 Window 可以还原、显示或激活。
    /// </summary>
    SingleViewHost
}

/// <summary>
///     远程抽取"把结果送到最前"这一步该做什么。
/// </summary>
/// <remarks>
///     <para>
///         之所以要单独一个纯函数：桌面与平板的宿主形态不同，而"没做置前"和"做了但做不到"看起来一模一样。
///         把决策抽出来，测试就能钉住**平板不会去碰一个根本不存在的窗口**，也不必真的跑一台平板。
///     </para>
///     <para>
///         <b>平板能做什么、不能做什么</b>：单窗口宿主里主界面已经是当前页，因此
///         "把结果送到最前"只剩两件能做的事——把目标页切到主界面里（调用方做）、以及激活视图会话。
///         <b>把整个应用从后台提到前台是做不到的</b>：Android 与 iPadOS 都不允许应用自行这么做，
///         这不是本项目的降级，而是平台约束。应用已经在前台时（教室里那台平板常驻在抽取页上），
///         结果会立刻可见。
///     </para>
/// </remarks>
/// <param name="Surface">结果落在哪种宿主上。</param>
/// <param name="Restore">桌面：窗口最小化时需要先还原。</param>
/// <param name="Show">桌面：窗口当前不可见时需要显示。</param>
/// <param name="Activate">桌面：是否激活到前台（这条路径上永远为真）。</param>
public readonly record struct RemoteDrawFocusPlan(RemoteDrawSurface Surface, bool Restore, bool Show, bool Activate)
{
    /// <summary>按宿主形态决定"置前"要做哪几件事。</summary>
    /// <remarks>
    ///     单窗口宿主上一律返回"什么都不做"：那里没有窗口状态可读，
    ///     <see cref="RemoteDrawSurface.SingleViewHost" /> 本身就是"别再去找窗口"的指令。
    /// </remarks>
    public static RemoteDrawFocusPlan Resolve(bool isDesktop, bool isWindowVisible, bool isWindowMinimized)
    {
        if (!isDesktop)
            return new RemoteDrawFocusPlan(RemoteDrawSurface.SingleViewHost, false, false, false);

        var window = RemoteDrawWindowPlan.Resolve(isWindowVisible, isWindowMinimized);
        return new RemoteDrawFocusPlan(
            RemoteDrawSurface.DesktopWindow,
            window.Restore,
            window.Show,
            window.Activate);
    }
}

/// <summary>
///     用两个抽取页面 ViewModel 真正执行一次远程抽取。
/// </summary>
/// <remarks>
///     <para>
///         <b>必须在 UI 线程上执行</b>：抽取会改 <c>ObservableObject</c> 状态并驱动滚动动画，
///         从线程池改它们会让绑定层收到别的线程发来的通知（集控命令正是在线程池上执行的）。
///     </para>
///     <para>
///         <b>结果要落在教室里看得见的地方</b>：快抽打开快抽窗，点名把主窗口**还原、显示并激活**。
///         远程抽取如果只是"窗口在后台刷新了一下"，老师看到的仍是一个没变化的桌面——
///         既不符合课堂用途，也让"抽了谁"无法当场核对。快抽窗保持既有行为：它本身就是置顶工具窗，
///         契约是"不抢焦点"，因此不在这条置前逻辑里。
///     </para>
///     <para>
///         页面 ViewModel 在这里才解析（<see cref="IAppHost.GetService{T}" />）：Host 在线程池线程上构造
///         托管服务，在构造函数里创建界面对象会把它绑到错误的线程上。
///     </para>
/// </remarks>
public sealed class ControlPageDrawExecutor : IControlDrawExecutor
{
    public Task<ControlDrawExecution> DrawQuickAsync(CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var quickDraw = IAppHost.GetService<QuickDrawPageViewModel>();
            App.ShowQuickDrawWindow();

            var outcome = await quickDraw.StartRemoteDrawAsync(cancellationToken).ConfigureAwait(true);
            return ControlDrawExecutionFactory.From(
                outcome,
                ControlDrawTriggerRequest.TargetQuick,
                outcome.ListName ?? quickDraw.SelectedStudentListName);
        });

    public Task<ControlDrawExecution> DrawRollCallAsync(
        ControlDrawTriggerRequest request,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var rollCall = IAppHost.GetService<RollCallPageViewModel>();

            // 先把点名页送到最前面，再开始抽：动画与结果都要发生在老师已经能看到的那个窗口里。
            await App.ShowMainWindowForRemoteDrawAsync("main.rollCall").ConfigureAwait(true);

            var outcome = await rollCall.StartRemoteDrawAsync(request, cancellationToken).ConfigureAwait(true);
            return ControlDrawExecutionFactory.From(
                outcome,
                ControlDrawTriggerRequest.TargetRollCall,
                outcome.ListName ?? request.ListName);
        });

    public Task<ControlDrawExecution> DrawLotteryAsync(
        ControlDrawTriggerRequest request,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var lottery = IAppHost.GetService<LotteryPageViewModel>();

            // 与点名同一条展示逻辑，只是切到抽奖页：远程抽奖的结果同样必须落在教室看得见的那一页上。
            await App.ShowMainWindowForRemoteDrawAsync("main.lottery").ConfigureAwait(true);

            var outcome = await lottery.StartRemoteDrawAsync(request, cancellationToken).ConfigureAwait(true);
            return ControlDrawExecutionFactory.From(
                outcome,
                ControlDrawTriggerRequest.TargetLottery,
                outcome.ListName ?? request.ListName);
        });
}
