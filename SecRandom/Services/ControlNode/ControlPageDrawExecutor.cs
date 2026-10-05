using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.ControlNode;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     用两个抽取页面 ViewModel 真正执行一次远程抽取。
/// </summary>
/// <remarks>
///     <para>
///         <b>必须在 UI 线程上执行</b>：抽取会改 <c>ObservableObject</c> 状态并驱动滚动动画，
///         从线程池改它们会让绑定层收到别的线程发来的通知（集控命令正是在线程池上执行的）。
///     </para>
///     <para>
///         <b>结果要落在教室里看得见的地方</b>：快抽打开快抽窗，点名把主窗口切到点名页。
///         远程抽取如果静默完成，老师只会看到一个"抽过了"的控制台记录，而全班什么都没看到——
///         这既不符合课堂用途，也让"抽了谁"无法当场核对。
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

            // 点名结果长在点名页上：先把主窗口与那一页切出来，再开始抽。
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.rollCall");

            var outcome = await rollCall.StartRemoteDrawAsync(request, cancellationToken).ConfigureAwait(true);
            return ControlDrawExecutionFactory.From(
                outcome,
                ControlDrawTriggerRequest.TargetRollCall,
                outcome.ListName ?? request.ListName);
        });
}
