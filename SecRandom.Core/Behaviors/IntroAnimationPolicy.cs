namespace SecRandom.Core.Behaviors;

/// <summary>
///     入场/弹出动画的进程级总开关，由低配模式设置驱动。
///     <para>
///         只能在 <c>IsIntroAnimationEnabled</c> 首次生效之前拦截：这两个行为的附加属性
///         （<c>CanPlayAnimation</c>/<c>IsAnimationPlayed</c>）一旦置位就不再复位，而
///         <c>StackPanelIntroAnimation.axaml</c> 会先给子元素设 <c>Opacity=0</c>、再靠动画拉回 1。
///         若在置位之后再关闭动画，子元素会永久停在 <c>Opacity=0</c> 而不显示，所以这里只在
///         「是否给子元素打标记」这一个点上拦截，绝不阻止已经开始的动画跑完。
///     </para>
///     <para>
///         关闭时不会追溯已经打开的页面：这些页面的子元素从未被打上标记，内容按默认
///         <c>Opacity=1</c> 正常显示，不需要重新加载。
///     </para>
/// </summary>
public static class IntroAnimationPolicy
{
    /// <summary>是否播放入场/弹出动画，默认开启</summary>
    public static bool IsEnabled { get; set; } = true;
}
