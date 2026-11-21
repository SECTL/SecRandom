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
///     算出来的；整体透明度也会淡化文字，因此低不透明度时只能选择对比度较高的黑/白，不能保证达标。
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

    /// <summary>整块卡片（含文字）的不透明度；手动构造方案时默认不透明。</summary>
    public double SurfaceOpacity { get; init; } = 1;

    /// <summary>计算中位数处可见文字亮度所需的桌面亮度。</summary>
    public double BackdropMedianLuminance { get; init; }
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
    /// <summary>正文与卡片底之间的目标对比度（WCAG AA 正文标准）；整体透明度可能使其无法达到。</summary>
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
        var luminance = RelativeLuminance(surface);
        return Build(isDarkTheme, 1.0, BackdropLuminance.FromPercentiles(luminance, luminance, luminance));
    }

    /// <summary>
    ///     按背景亮度分布求解自适应方案。<paramref name="appThemeIsDark" /> 只在亮度中位数恰好
    ///     落在分界线上时充当平局判定，正常情况完全由桌面决定。
    /// </summary>
    /// <param name="surfaceOpacity">
    ///     整块卡片在窗口里的实际不透明度（0–1）。通知窗的「不透明度」设置作用在卡片整体上，
    ///     所以桌面会按 <c>1 - 不透明度</c> 的比例透上来，判定必须把这一层也算进去，否则
    ///     低不透明度下会出现"白字压在被桌面冲淡的浅卡片上"。
    /// </param>
    /// <remarks>
    ///     半透明卡片压在一张花壁纸/正在放的 PPT 上时，卡片左边缘与右边缘的最终亮度可能相差很大，
    ///     所以前景不能只对中位数成立：它必须对 <see cref="BackdropLuminance.P10" /> 与
    ///     <see cref="BackdropLuminance.P90" /> 合成出来的两端都尽量清晰。两端做不到就依次把底色
    ///     压得更实，最后到底色不透明；整体透明度仍按用户设置保留，文字与底色都会被桌面冲淡，
    ///     所以最终只能选择最差端点对比度较高的黑/白，不能承诺一定达到目标。
    /// </remarks>
    public static AdaptiveTheme Resolve(
        BackdropLuminance backdrop,
        bool appThemeIsDark,
        double surfaceOpacity = 1)
    {
        var opacity = NormalizeOpacity(surfaceOpacity);
        var median = backdrop.Median;
        var isDark = Math.Abs(median - DarkBackdropThreshold) < 0.0001
            ? appThemeIsDark
            : median < DarkBackdropThreshold;

        var alpha = backdrop.Spread >= HighSpreadThreshold
            ? HighSpreadSurfaceAlpha
            : DefaultSurfaceAlpha;

        var theme = Build(isDark, alpha, backdrop, opacity);
        var bestContrast = WorstContrastAcrossBackdrop(theme, backdrop, opacity);
        if (bestContrast >= MinimumContrastRatio)
            return theme;

        // 整体透明度会同时淡化文字；各档都不达标时保留最差端点对比度最高的方案。
        var stronger = Build(isDark, ContrastGateSurfaceAlpha, backdrop, opacity);
        var strongerContrast = WorstContrastAcrossBackdrop(stronger, backdrop, opacity);
        if (strongerContrast >= MinimumContrastRatio)
            return stronger;
        if (strongerContrast > bestContrast)
        {
            theme = stronger;
            bestContrast = strongerContrast;
        }

        var opaqueSurface = Build(isDark, 1.0, backdrop, opacity);
        return WorstContrastAcrossBackdrop(opaqueSurface, backdrop, opacity) >= bestContrast ? opaqueSurface : theme;
    }

    /// <summary>
    ///     按"合成之后的卡片底有多亮"选纯黑或纯白：这就是需求本身（浅底深字、深底白字），
    ///     也保证了无论采样/透明度/主题怎么组合，字色都不会和眼睛看到的底色唱反调。
    /// </summary>
    public static Color SelectForeground(double surfaceLuminance) =>
        SelectForeground(surfaceLuminance, surfaceLuminance);

    /// <summary>
    ///     卡片底在两端的亮度并不相同（<paramref name="darkestComposite" /> 到
    ///     <paramref name="lightestComposite" />）时，取"最差那一端仍看得最清"的颜色。
    /// </summary>
    public static Color SelectForeground(double darkestComposite, double lightestComposite)
    {
        var dark = NormalizeLuminance(darkestComposite);
        var light = Math.Max(dark, NormalizeLuminance(lightestComposite));

        // 不透明黑字在暗端最吃亏，不透明白字在亮端最吃亏。
        var blackWorst = ContrastRatio(dark, Colors.Black);
        var whiteWorst = ContrastRatio(light, Colors.White);
        return blackWorst >= whiteWorst ? Colors.Black : Colors.White;
    }

    /// <summary>
    ///     一个实色底盖在给定亮度的背景上之后的亮度：<paramref name="surfaceOpacity" /> 是整块
    ///     卡片的不透明度，<paramref name="surfaceAlpha" /> 是底色自身的透明度。
    /// </summary>
    public static double ComposeSurfaceLuminance(
        Color surface,
        double surfaceAlpha,
        double backdropLuminance,
        double surfaceOpacity = 1)
    {
        var opacity = NormalizeOpacity(surfaceOpacity);
        var alpha = NormalizeOpacity(surfaceAlpha) * opacity;
        return Math.Clamp(
            alpha * RelativeLuminance(surface) + (1 - alpha) * NormalizeLuminance(backdropLuminance), 0, 1);
    }

    /// <summary>中位数处可见文字与可见卡片底的对比度，包含整块卡片的不透明度。</summary>
    public static double ContrastAgainstSurface(AdaptiveTheme theme) =>
        ContrastRatio(
            theme.SurfaceLuminance,
            ComposeForegroundLuminance(theme.Foreground, theme.BackdropMedianLuminance, theme.SurfaceOpacity));

    /// <summary>
    ///     前景色在这张卡片上最差的那一处的对比度：卡片半透明时，背景的亮端与暗端会给出不同的
    ///     最终底色，两端都要达标才算真的看得清。
    /// </summary>
    public static double WorstContrastAcrossBackdrop(AdaptiveTheme theme, BackdropLuminance backdrop) =>
        WorstContrastAcrossBackdrop(theme, backdrop, theme.SurfaceOpacity);

    public static double WorstContrastAcrossBackdrop(
        AdaptiveTheme theme,
        BackdropLuminance backdrop,
        double surfaceOpacity)
    {
        var opacity = NormalizeOpacity(surfaceOpacity);
        var darkest = ComposeSurfaceLuminance(theme.Surface, theme.SurfaceAlpha, backdrop.P10, opacity);
        var lightest = ComposeSurfaceLuminance(theme.Surface, theme.SurfaceAlpha, backdrop.P90, opacity);
        return Math.Min(
            ContrastRatio(darkest, ComposeForegroundLuminance(theme.Foreground, backdrop.P10, opacity)),
            ContrastRatio(lightest, ComposeForegroundLuminance(theme.Foreground, backdrop.P90, opacity)));
    }

    /// <summary>WCAG 相对亮度（输入已经是 0–1 线性亮度）。</summary>
    public static double ContrastRatio(double backgroundLuminance, Color foreground) =>
        ContrastRatio(backgroundLuminance, RelativeLuminance(foreground));

    private static double ContrastRatio(double backgroundLuminance, double foregroundLuminance)
    {
        var lighter = Math.Max(backgroundLuminance, foregroundLuminance);
        var darker = Math.Min(backgroundLuminance, foregroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double ComposeForegroundLuminance(Color foreground, double backdropLuminance, double surfaceOpacity)
    {
        var opacity = NormalizeOpacity(surfaceOpacity);
        return opacity * RelativeLuminance(foreground) + (1 - opacity) * NormalizeLuminance(backdropLuminance);
    }

    /// <summary>sRGB 颜色 → 线性相对亮度（WCAG 定义）。</summary>
    public static double RelativeLuminance(Color color) =>
        0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    private static AdaptiveTheme Build(
        bool isDark,
        double alpha,
        BackdropLuminance backdrop,
        double surfaceOpacity = 1)
    {
        var surfaceBase = isDark ? DarkSurfaceColor : LightSurfaceColor;

        // 使用实际输出的 8 位 alpha 计算，确保选色和对比度检查针对同一块底色。
        var surface = WithAlpha(surfaceBase, (byte)Math.Round(alpha * 255));
        var composited = ComposeSurfaceLuminance(surface, surface.A / 255d, backdrop.Median, surfaceOpacity);
        var theme = new AdaptiveTheme(
            IsDarkBackdrop: isDark,
            Foreground: Colors.Black,
            SecondaryForeground: default,
            Stroke: default,
            Surface: surface,
            CardSurface: WithAlpha(surfaceBase, (byte)Math.Round(Math.Min(1.0, alpha + CardSurfaceAlphaBonus) * 255)),
            AvatarSurface: default,
            SurfaceLuminance: composited)
        {
            SurfaceOpacity = NormalizeOpacity(surfaceOpacity),
            BackdropMedianLuminance = NormalizeLuminance(backdrop.Median)
        };

        // 整体透明度同时作用在字和底上。比较两种可见字色在两个端点的最差对比度。
        var blackWorst = WorstContrastAcrossBackdrop(theme, backdrop, surfaceOpacity);
        var whiteWorst = WorstContrastAcrossBackdrop(theme with { Foreground = Colors.White }, backdrop, surfaceOpacity);
        var foreground = blackWorst >= whiteWorst ? Colors.Black : Colors.White;
        return theme with
        {
            Foreground = foreground,
            SecondaryForeground = WithAlpha(foreground, SecondaryAlpha),
            Stroke = WithAlpha(foreground, StrokeAlpha),
            AvatarSurface = WithAlpha(foreground, AvatarAlpha)
        };
    }

    private static double NormalizeOpacity(double value)
    {
        if (double.IsNaN(value))
            return 1;

        return Math.Clamp(value, 0, 1);
    }

    private static double NormalizeLuminance(double value)
    {
        if (double.IsNaN(value))
            return 0;

        return Math.Clamp(value, 0, 1);
    }

    private static Color WithAlpha(Color color, byte alpha) =>
        Color.FromArgb(alpha, color.R, color.G, color.B);

    private static double Linearize(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}