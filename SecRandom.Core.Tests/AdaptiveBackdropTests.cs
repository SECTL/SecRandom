using Avalonia.Media;
using SecRandom.Core.Services.Presentation;

namespace SecRandom.Core.Tests;

public class AdaptiveBackdropTests
{
    [Theory]
    [InlineData(0.00)]
    [InlineData(0.10)]
    [InlineData(0.35)]
    [InlineData(0.50)]
    [InlineData(0.65)]
    [InlineData(0.90)]
    [InlineData(1.00)]
    public void KeepsReadableContrastAcrossTheWholeLuminanceRange(double median)
    {
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(median, median, median), appThemeIsDark: false);

        Assert.True(
            AdaptiveBackdropResolver.ContrastAgainstSurface(theme) >= AdaptiveBackdropResolver.MinimumContrastRatio,
            $"median={median:0.00} contrast={AdaptiveBackdropResolver.ContrastAgainstSurface(theme):0.00}");
    }

    [Fact]
    public void PicksWhiteTextOnADarkDesktop()
    {
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.01, 0.05, 0.09), appThemeIsDark: false);

        Assert.True(theme.IsDarkBackdrop);
        Assert.Equal(Colors.White, theme.Foreground);
    }

    [Fact]
    public void PicksBlackTextOnABrightDesktop()
    {
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.85, 0.93, 0.99), appThemeIsDark: true);

        Assert.False(theme.IsDarkBackdrop);
        Assert.Equal(Colors.Black, theme.Foreground);
    }

    [Fact]
    public void PressesTheSurfaceHarderWhenTheBackdropIsBusy()
    {
        var flat = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.30, 0.35, 0.40), appThemeIsDark: false);
        var busy = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.02, 0.35, 0.95), appThemeIsDark: false);

        Assert.True(busy.SurfaceAlpha > flat.SurfaceAlpha);
    }

    [Fact]
    public void CardSurfaceIsAtLeastAsOpaqueAsTheRootSurface()
    {
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.2, 0.3, 0.4), appThemeIsDark: false);

        Assert.True(theme.CardSurface.A >= theme.Surface.A);
        Assert.True(theme.Surface.A < 255);
    }

    [Fact]
    public void FallsBackToAnOpaqueCardMatchingTheAppTheme()
    {
        var dark = AdaptiveBackdropResolver.FromAppTheme(isDarkTheme: true);
        var light = AdaptiveBackdropResolver.FromAppTheme(isDarkTheme: false);

        Assert.Equal(255, dark.Surface.A);
        Assert.Equal(255, light.Surface.A);
        Assert.Equal(Colors.White, dark.Foreground);
        Assert.Equal(Colors.Black, light.Foreground);
    }

    [Fact]
    public void ContrastUsesTheWcagFormula()
    {
        Assert.Equal(21.0, AdaptiveBackdropResolver.ContrastRatio(0, Colors.White), 3);
        Assert.Equal(1.0, AdaptiveBackdropResolver.ContrastRatio(0.0, Colors.Black), 3);
    }

    /// <summary>
    ///     需求本身：浅底必须黑字、深底必须白字，判据是"画出来的卡片有多亮"，不是"桌面深不深"。
    /// </summary>
    [Theory]
    [InlineData(0.00)]
    [InlineData(0.18)]
    [InlineData(0.50)]
    [InlineData(0.82)]
    [InlineData(1.00)]
    public void SelectsTheHigherContrastInkForTheDrawnSurface(double surfaceLuminance)
    {
        var ink = AdaptiveBackdropResolver.SelectForeground(surfaceLuminance);

        Assert.True(
            AdaptiveBackdropResolver.ContrastRatio(surfaceLuminance, ink) >=
            AdaptiveBackdropResolver.ContrastRatio(
                surfaceLuminance,
                ink == Colors.Black ? Colors.White : Colors.Black));
        Assert.Equal(surfaceLuminance >= 0.1792 ? Colors.Black : Colors.White, ink);
    }

    /// <summary>整体不透明度也淡化文字：中灰桌面上的淡深色卡片仍以白字较清晰，但无法达到 AA。</summary>
    [Fact]
    public void WashedOutDarkCardComparesTheVisibleInkInsteadOfOpaqueInk()
    {
        var backdrop = BackdropLuminance.FromPercentiles(0.5, 0.5, 0.5);
        var theme = AdaptiveBackdropResolver.Resolve(backdrop, appThemeIsDark: true, surfaceOpacity: 0.2);

        Assert.True(theme.IsDarkBackdrop);
        Assert.Equal(Colors.White, theme.Foreground);
        Assert.Equal(1.0, theme.SurfaceAlpha);
        // 独立数值：L(#1F)=0.0137020830472897，底=.2*L(#1F)+.8*.5，白字=.2+.8*.5。
        Assert.Equal(0.402740416609458, theme.SurfaceLuminance, 12);
        Assert.Equal(1.43570128964365, AdaptiveBackdropResolver.ContrastAgainstSurface(theme), 12);
        Assert.Equal(1.43570128964365,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop), 12);
        Assert.Equal(1.00608981468768,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme with { Foreground = Colors.Black }, backdrop), 12);
        Assert.True(AdaptiveBackdropResolver.ContrastAgainstSurface(theme) < AdaptiveBackdropResolver.MinimumContrastRatio);
    }

    /// <summary>合成亮度必须等于"卡片色 × 卡片不透明度 × 整块不透明度 + 剩下的桌面"。</summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(0.8)]
    [InlineData(0.5)]
    [InlineData(0.2)]
    public void SurfaceLuminanceMatchesTheCompositionOfTheDrawnCard(double opacity)
    {
        const double backdropLuminance = 1.0;
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(backdropLuminance, backdropLuminance, backdropLuminance),
            appThemeIsDark: false,
            surfaceOpacity: opacity);

        var expected = AdaptiveBackdropResolver.ComposeSurfaceLuminance(
            theme.Surface, theme.SurfaceAlpha, backdropLuminance, opacity);

        Assert.Equal(expected, theme.SurfaceLuminance, 12);
        // 文字也乘整体不透明度，白桌面上的黑字亮度为 1-opacity。
        var expectedContrast = (expected + 0.05) / (1 - opacity + 0.05);
        Assert.Equal(expectedContrast, AdaptiveBackdropResolver.ContrastAgainstSurface(theme), 12);
        if (opacity <= 0.5)
            Assert.True(expectedContrast < AdaptiveBackdropResolver.MinimumContrastRatio);
    }

    /// <summary>
    ///     卡片半透明压在一张花壁纸/PPT 上时，卡片两端会合成出不同的底色；字必须在两端都看得清，
    ///     否则就是"左边看得见、右边消失"。
    /// </summary>
    [Theory]
    [InlineData(0.02, 0.30, 0.62)]
    [InlineData(0.05, 0.50, 0.98)]
    [InlineData(0.10, 0.75, 0.95)]
    public void KeepsTheInkReadableAtBothEndsOfABusyBackdrop(double p10, double median, double p90)
    {
        const double opacity = 0.9;
        var backdrop = BackdropLuminance.FromPercentiles(p10, median, p90);
        var theme = AdaptiveBackdropResolver.Resolve(backdrop, appThemeIsDark: false, surfaceOpacity: opacity);

        var worst = AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop, opacity);

        Assert.True(
            worst >= AdaptiveBackdropResolver.MinimumContrastRatio,
            $"p10={p10:0.00} median={median:0.00} p90={p90:0.00} worst={worst:0.00} " +
            $"ink={(theme.Foreground == Colors.Black ? "black" : "white")} alpha={theme.SurfaceAlpha:0.00}");
    }

    /// <summary>
    ///     不透明度低到"桌面透上来太多"时，任何纯色都达不到 4.5，此时只能选对比更高的那个，
    ///     并且卡片底必须已经被压到不透明（在这条不透明度下已经无解，但仍要是最优解）。
    /// </summary>
    [Fact]
    public void PicksTheBestAvailableInkWhenTheCardIsTooTransparentToBeReadable()
    {
        const double opacity = 0.75;
        var backdrop = BackdropLuminance.FromPercentiles(0, 0.2, 1);
        var theme = AdaptiveBackdropResolver.Resolve(backdrop, appThemeIsDark: false, surfaceOpacity: opacity);

        Assert.Equal(1.0, theme.SurfaceAlpha, 3);
        Assert.Equal(Colors.White, theme.Foreground);
        // 整体透明度同时淡化文字，黑/白字在亮桌面端的可见对比度都不足 4.5。
        Assert.Equal(3.384077715267313,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop, opacity), 12);
        Assert.Equal(1.0342552076182241,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme with { Foreground = Colors.Black }, backdrop, opacity), 12);
        Assert.True(
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop, opacity) >=
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(
                theme with { Foreground = Colors.Black },
                backdrop,
                opacity));
        Assert.True(
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop, opacity) >=
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(
                theme with { Foreground = Colors.White },
                backdrop,
                opacity));
    }

    /// <summary>整体透明度不改变桌面分类，但可能让较清晰的白字也达不到目标对比度。</summary>
    [Fact]
    public void SurfaceOpacityDoesNotReclassifyTheBackdrop()
    {
        var theme = AdaptiveBackdropResolver.Resolve(
            BackdropLuminance.FromPercentiles(0.45, 0.45, 0.45), appThemeIsDark: true, surfaceOpacity: 0.05);

        Assert.True(theme.IsDarkBackdrop);
        Assert.Equal(Colors.White, theme.Foreground);
        Assert.True(AdaptiveBackdropResolver.ContrastAgainstSurface(theme) < AdaptiveBackdropResolver.MinimumContrastRatio);
    }

    [Fact]
    public void KeepsTheBetterTranslucentSurfaceWhenNoCandidateMeetsTheContrastTarget()
    {
        var backdrop = BackdropLuminance.FromPercentiles(0, 0.8, 1);
        var theme = AdaptiveBackdropResolver.Resolve(backdrop, appThemeIsDark: false, surfaceOpacity: 0.75);

        Assert.Equal(Colors.Black, theme.Foreground);
        Assert.Equal(224, theme.Surface.A);
        // 白底本身越实，亮桌面上的淡黑字反而越贴近底色；不能无条件返回 alpha=1。
        Assert.Equal(3.4033140306651,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(theme, backdrop), 12);
        var opaqueSurface = theme with { Surface = Color.FromRgb(0xFA, 0xFA, 0xFA) };
        Assert.Equal(3.38993338312322,
            AdaptiveBackdropResolver.WorstContrastAcrossBackdrop(opaqueSurface, backdrop), 12);
    }
}