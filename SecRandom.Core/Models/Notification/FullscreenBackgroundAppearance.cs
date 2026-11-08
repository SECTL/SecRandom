using Avalonia.Media;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.Notification;

/// <summary>
///     全屏结果渠道的背景"算出来的样子"：模式已归一化、颜色已解析成 <see cref="Color" />。
/// </summary>
/// <remarks>
///     <para>
///         渲染层只认这个记录，不碰配置——配置里存的是 <see cref="Color" /> 或字符串，夹取与兜底的逻辑
///         全部收在 <see cref="SubConfigs.FullscreenNotificationSettings.ResolveBackgroundAppearance" /> 一处，
///         窗口后台代码不再自己解析一遍。
///     </para>
///     <para>
///         图片模式不在这里携带图片路径：图片落在数据目录、由窗口按文件名解析（见
///         <c>FullscreenNotificationWindow.ResolveBackgroundPath</c>），Core 不碰文件系统。
///     </para>
/// </remarks>
/// <param name="Mode">背景模式（已归一化）。</param>
/// <param name="SolidColor">纯色模式的底色；其余模式为 <c>null</c>。</param>
/// <param name="GradientStart">渐变起点色；非渐变模式为 <c>null</c>。</param>
/// <param name="GradientEnd">渐变终点色；非渐变模式为 <c>null</c>。</param>
/// <param name="GradientAngle">渐变角度（度，0 = 从左往右，顺时针）。</param>
public sealed record FullscreenBackgroundAppearance(
    FullscreenBackgroundMode Mode,
    Color? SolidColor,
    Color? GradientStart,
    Color? GradientEnd,
    int GradientAngle)
{
    /// <summary>跟随主题——渲染层不铺任何颜色层，让窗口自己的主题底色透上来。</summary>
    public static FullscreenBackgroundAppearance Theme { get; } =
        new(FullscreenBackgroundMode.Theme, null, null, null, 135);

    /// <summary>要不要真的去读一张背景图。</summary>
    public bool UsesImage => Mode == FullscreenBackgroundMode.Image;
}