namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     全屏结果渠道的背景来源。
/// </summary>
/// <remarks>
///     <para>
///         默认跟随主题：整屏底色由应用主题与强调色推出来，不依赖任何用户文件。
///         纯色与双色渐变是"投影场景"的常见需求（教室投影仪上深色底比花哨图片更清楚），
///         图片模式才需要导入文件，并且只有它才显示模糊与不透明度。
///     </para>
///     <para>
///         设置页按模式逐张显隐卡片（跟随时一张颜色卡都不露出来），与看板类项目的背景设置保持同一套交互。
///         枚举值是<b>持久化</b>的，只能追加、不能重排既有含义。
///     </para>
/// </remarks>
public enum FullscreenBackgroundMode
{
    /// <summary>跟随主题（默认）。</summary>
    Theme = 0,

    /// <summary>自定义纯色。</summary>
    Solid = 1,

    /// <summary>自定义双色渐变。</summary>
    Gradient = 2,

    /// <summary>自定义背景图。</summary>
    Image = 3
}