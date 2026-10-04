using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Media.Animation;
using FluentAvalonia.UI.Navigation;
using SecRandom.Core.Behaviors;

namespace SecRandom.Helpers;

/// <summary>
///     低配模式下的界面动效策略。
///     <para>
///         主界面与设置界面的内容宿主是 FluentAvalonia 的 <see cref="FAFrame" />，它默认会为每次导航播放
///         入场过渡（整页淡入/位移）。这段动画来自框架而不是本项目的样式，所以低配模式必须在这里显式换成
///         <see cref="FASuppressNavigationTransitionInfo" />，否则"关闭切页动画"看起来完全没生效。
///     </para>
///     <para>
///         正常模式下仍然调用 FluentAvalonia 的单参数导航重载，保持它自己的导航栈语义不变。
///     </para>
/// </summary>
internal static class UiMotion
{
    public static bool IsReduced => !IntroAnimationPolicy.IsEnabled;

    /// <summary>
    ///     低配模式下不再使用 Mica 半透明窗口背景：它会让 DWM 每帧参与背景合成。
    ///     窗口合成级别只能在构造期决定，所以这一项需要重启生效。
    /// </summary>
    public static bool UseWindowCompositionEffects => !IsReduced;

    /// <summary>按当前动效策略导航到目标。</summary>
    public static void NavigateFromObject<T>(FAFrame frame, T target)
    {
        if (!IsReduced)
        {
            frame.NavigateFromObject(target);
            return;
        }

        frame.NavigateFromObject(target, new FAFrameNavigationOptions
        {
            IsNavigationStackEnabled = frame.IsNavigationStackEnabled,
            TransitionInfoOverride = new FASuppressNavigationTransitionInfo()
        });
    }
}
