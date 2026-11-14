using Avalonia.Media;

namespace SecRandom.Core.Services.Presentation;

/// <summary>
///     一块背景的亮度分布（线性相对亮度，0–1）。
/// </summary>
/// <remarks>
///     这是 <see cref="SecRandom.Platforms.Abstractions.ScreenLuminanceSample" /> 在 Core 侧的中性投影。
///     Core 不引用平台工程，所以平台采样结果必须先降成这三个数再交给判定逻辑；这也让判定逻辑
///     可以完全不碰 Avalonia 之外的任何东西，直接单测。
/// </remarks>
public readonly record struct BackdropLuminance(double P10, double Median, double P90)
{
    public double Spread => Math.Max(0, P90 - P10);

    public static BackdropLuminance FromPercentiles(double p10, double median, double p90) =>
        new(Clamp01(p10), Clamp01(median), Clamp01(p90));

    private static double Clamp01(double value) => double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);
}

/// <summary>
///     一次展示使用的自适应前景/底色方案。
/// </summary>
/// <remarks>
///     <see cref="Surface" /> 与 <see cref="CardSurface" /> 都带透明度：卡片压在采样到的桌面上，
///     而不是替换成一块不透明色，这样"自适应"才真的看得见桌面。前景色是针对合成之后的结果
///     算出来的，所以它对 <see cref="SurfaceLuminance" /> 的对比度一定达标。
/// </remarks>
public sealed record AdaptiveTheme(
    bool IsDarkBackdrop,
    Color Foreground,
    Color SecondaryForeground,
    Color Stroke,
    Color Surface,
    Color CardSurface,
    Color AvatarSurface,
    double SurfaceLuminance)
{
    /// <summary>卡片底色的不透明度（0–1）。</summary>
    public double SurfaceAlpha => Surface.A / 255d;
}

/// <summary>
///     把"背景有多亮"翻译成"用白字还是黑字、卡片底压多深"。
/// </summary>
/// <remarks>
///     <para>
///         只用两种前景（白/黑）是刻意的：这正是"自适应主题色"的需求本身，也是唯一在 HDR/广色域/
///         远程桌面下都稳的判据。所有中间色、强调色都由既有主题体系负责，这里不引入第三种前景。
///     </para>
///     <para>
///         判定顺序：先按中位数选深/浅，再用亮度离散度决定要不要把底压得更实（花壁纸、PPT 视频背景
///         的亮度跨度很大，同一个中位数下文字可能局部不可读），最后用 WCAG 对比度兜一次底。
///     </para>
/// </remarks>
public static class AdaptiveBackdropResolver
{
    /// <summary>正文与卡片底之间必须达到的对比度（WCAG AA 正文标准）。</summary>
    public const double MinimumContrastRatio = 4.5;

    /// <summary>中位数低于它就算深色背景。</summary>
    public const double DarkBackdropThreshold = 0.5;

    /// <summary>背景亮度跨度过大时把卡片底压得更实，避免局部文字与背景同色。</summary>
    public const double HighSpreadThreshold = 0.5;

    public const double DefaultSurfaceAlpha = 0.82;
    public const double HighSpreadSurfaceAlpha = 0.88;
    public const double ContrastGateSurfaceAlpha = 0.92;

    private const double CardSurfaceAlphaBonus = 0.08;
    private const byte SecondaryAlpha = 0xCC;
    private const byte StrokeAlpha = 0x26;
    private const byte AvatarAlpha = 0x33;

    private static readonly Color DarkSurfaceColor = Color.FromRgb(0x1F, 0x1F, 0x1F);
    private static readonly Color LightSurfaceColor = Color.FromRgb(0xFA, 0xFA, 0xFA);

    /// <summary>没有采样能力或采样失败时的回退方案：与应用主题一致的实色卡片（即今天的外观）。</summary>
    public static AdaptiveTheme FromAppTheme(bool isDarkTheme)
    {
        var surface = isDarkTheme ? DarkSurfaceColor : LightSurfaceColor;
        return Build(isDarkTheme, 1.0, RelativeLuminance(surface));
    }

    /// <summary>
    ///     按背景亮度分布求解自适应方案。<paramref name="appThemeIsDark" /> 只在亮度中位数恰好
    ///     落在分界线上时充当平局判定，正常情况完全由桌面决定。
    /// </summary>
    public static AdaptiveTheme Resolve(BackdropLuminance backdrop, bool appThemeIsDark)
    {
        var median = backdrop.Median;
        var isDark = Math.Abs(median - DarkBackdropThreshold) < 0.0001
            ? appThemeIsDark
            : median < DarkBackdropThreshold;

        var alpha = backdrop.Spread >= HighSpreadThreshold
            ? HighSpreadSurfaceAlpha
            : DefaultSurfaceAlpha;

        var theme = Build(isDark, alpha, median);
        if (ContrastAgainstSurface(theme) >= MinimumContrastRatio)
            return theme;

        // 对比度不够只可能出现在"半透明底 + 中灰背景"的窄带上：把底压得更实再算一次，
        // 还不够就直接不透明（此时回到应用主题的安全外观，但前景仍是按背景选的）。
        theme = Build(isDark, ContrastGateSurfaceAlpha, median);
        if (ContrastAgainstSurface(theme) >= MinimumContrastRatio)
            return theme;

        return Build(isDark, 1.0, median);
    }

    /// <summary>前景色与"卡片底合成到背景之后"的对比度。</summary>
    public static double ContrastAgainstSurface(AdaptiveTheme theme) =>
        ContrastRatio(theme.SurfaceLuminance, theme.Foreground);

    /// <summary>WCAG 相对亮度（输入已经是 0–1 线性亮度）。</summary>
    public static double ContrastRatio(double backgroundLuminance, Color foreground)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var lighter = Math.Max(backgroundLuminance, foregroundLuminance);
        var darker = Math.Min(backgroundLuminance, foregroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>sRGB 颜色 → 线性相对亮度（WCAG 定义）。</summary>
    public static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    private static AdaptiveTheme Build(bool isDark, double alpha, double backdropLuminance)
    {
        var surfaceBase = isDark ? DarkSurfaceColor : LightSurfaceColor;
        var foreground = isDark ? Colors.White : Colors.Black;

        // 卡片底 = 半透明色"贴"在背景上；我们能知道的只有背景亮度，所以用亮度做合成，
        // 不假装知道色相。这正是"只判黑白"在数学上成立的原因。
        var composited = Math.Clamp(
            alpha * RelativeLuminance(surfaceBase) + (1 - alpha) * backdropLuminance, 0, 1);

        var cardAlpha = Math.Min(1.0, alpha + CardSurfaceAlphaBonus);
        return new AdaptiveTheme(
            IsDarkBackdrop: isDark,
            Foreground: foreground,
            SecondaryForeground: WithAlpha(foreground, SecondaryAlpha),
            Stroke: WithAlpha(foreground, StrokeAlpha),
            Surface: WithAlpha(surfaceBase, (byte)Math.Round(alpha * 255)),
            CardSurface: WithAlpha(surfaceBase, (byte)Math.Round(cardAlpha * 255)),
            AvatarSurface: WithAlpha(foreground, AvatarAlpha),
            SurfaceLuminance: composited);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static double Linearize(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}