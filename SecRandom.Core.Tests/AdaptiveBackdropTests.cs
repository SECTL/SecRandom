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
}