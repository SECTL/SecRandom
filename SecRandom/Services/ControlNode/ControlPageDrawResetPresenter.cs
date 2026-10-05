using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.ControlNode;
using SecRandom.ViewModels.MainPages;

namespace SecRandom.Services.ControlNode;

/// <summary>远程重置要清展示态的那个页面。</summary>
public enum DrawResetSurface
{
    RollCall,
    Quick,
    Lottery
}

/// <summary>
///     远程重置目标 → 需要清展示态的页面。
/// </summary>
/// <remarks>
///     <para>
///         <b>点名与快抽要一起清</b>：两者共用同一份学生临时记录（既有约定），
///         只清其中一个页面的话，另一个页面还挂着上一轮抽到的人——数据已经归零、界面却在说谎。
///     </para>
///     <para>
///         抽成纯函数是为了能单测这条对应关系：它决定了"重置之后教室里还有没有旧结果"。
///     </para>
/// </remarks>
public static class ControlDrawResetTargets
{
    public static IReadOnlyList<DrawResetSurface> SurfacesFor(string target) =>
        string.Equals(target, ControlDrawResetRequest.TargetLottery, StringComparison.Ordinal)
            ? [DrawResetSurface.Lottery]
            : [DrawResetSurface.RollCall, DrawResetSurface.Quick];
}

/// <summary>
///     清掉远程重置所涉及的页面展示态。
/// </summary>
/// <remarks>
///     抽成接口是为了让 <see cref="ControlDrawResetHandler" /> 能被单测：真正的实现要解析界面 ViewModel，
///     而单测只需要验证"该清的时候清了、该拒绝的时候连数据都没清"。
/// </remarks>
public interface IControlDrawResetPresenter
{
    /// <summary>非交互闸门：需要本机验证时返回拒绝（远程命令绝不弹框）。</summary>
    RemoteDrawOutcome? EvaluateGate(ControlDrawResetRequest request);

    /// <summary>把对应页面的展示态恢复成新一轮（复用本地点一次重置的那条路径）。</summary>
    void ClearPresentation(ControlDrawResetRequest request);
}

/// <summary>
///     用三个抽取页面的 ViewModel 清展示态。
/// </summary>
/// <remarks>
///     <para>
///         与 <see cref="ControlPageDrawExecutor" /> 一样必须在 UI 线程上执行：它会改
///         <c>ObservableObject</c> 状态并刷新计数，从线程池改会让绑定层收到别的线程发来的通知。
///     </para>
///     <para>
///         闸门与清理是**两步**：先只判定（<see cref="EvaluateGate" />，不清任何东西），
///         数据清理完成后再调 <see cref="ClearPresentation" />。反过来做的话，
///         多页面目标里前一个页面已经清了、后一个页面判定失败，界面与数据就会对不上。
///     </para>
/// </remarks>
public sealed class ControlPageDrawResetPresenter : IControlDrawResetPresenter
{
    public RemoteDrawOutcome? EvaluateGate(ControlDrawResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OnUiThread(() =>
        {
            foreach (var surface in ControlDrawResetTargets.SurfacesFor(request.Target))
            {
                var rejection = surface switch
                {
                    DrawResetSurface.RollCall => IAppHost.GetService<RollCallPageViewModel>().EvaluateRemoteReset(),
                    DrawResetSurface.Quick => IAppHost.GetService<QuickDrawPageViewModel>().EvaluateRemoteReset(),
                    _ => IAppHost.GetService<LotteryPageViewModel>().EvaluateRemoteReset()
                };

                if (rejection is not null)
                    return rejection;
            }

            return null;
        });
    }

    public void ClearPresentation(ControlDrawResetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        OnUiThread<object?>(() =>
        {
            foreach (var surface in ControlDrawResetTargets.SurfacesFor(request.Target))
            {
                switch (surface)
                {
                    case DrawResetSurface.RollCall:
                        IAppHost.GetService<RollCallPageViewModel>().ResetRemotePresentation();
                        break;
                    case DrawResetSurface.Quick:
                        IAppHost.GetService<QuickDrawPageViewModel>().ResetRemotePresentation();
                        break;
                    default:
                        IAppHost.GetService<LotteryPageViewModel>().ResetRemotePresentation();
                        break;
                }
            }

            return null;
        });
    }

    private static T OnUiThread<T>(Func<T> action) =>
        Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.Invoke(action);
}
