namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     全屏结果通知渠道的动画样式。
/// </summary>
/// <remarks>
///     <para>
///         刻意不复用 <see cref="DrawAnimationStyleMode" />：那一套由抽取页的结果动画 switch 驱动，
///         还有回退策略与既有测试钉着，加值会改变既有抽取页的行为。全屏渠道是独立的窗口与编排，
///         动画枚举也应当独立。
///     </para>
///     <para>
///         数值按数字落盘，不要重排：<c>2</c> 留给已移除的转盘，旧配置在
///         <c>FullscreenNotificationSettings.AnimationStyle</c> 的 setter 里归一到
///         <see cref="Grid" />。任何新样式都从 <c>4</c> 往后排，避免旧机器把新值读成别的动画。
///     </para>
/// </remarks>
public enum FullscreenAnimationStyle
{
    /// <summary>直接显示：零动画，同帧出现结果帧。</summary>
    Direct = 0,

    /// <summary>打字机：逐字吐出文案（默认）。</summary>
    Typewriter = 1,

    // 2 曾为 Wheel（转盘）。转盘最多只能放 16 个扇区、多结果时指针也只能指向一个人，
    // 已由学号网格取代；旧值 2 读盘时回落为 Grid，见 FullscreenNotificationSettings.AnimationStyle。

    /// <summary>学号网格：按剩余名单排布，随机高亮后定格在结果上。</summary>
    Grid = 3
}