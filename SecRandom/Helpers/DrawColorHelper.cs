using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Helpers;

internal static class DrawColorHelper
{
    private const double MinSaturation = 0.7;
    private const double MaxSaturation = 1.0;
    private const double MinValue = 0.7;
    private const double MaxValue = 1.0;

    public static IBrush? ResolveAccentBrush(
        AnimationColorThemeMode colorTheme,
        Color fixedColor,
        ThemeMode appTheme)
    {
        return colorTheme switch
        {
            AnimationColorThemeMode.Fixed => new SolidColorBrush(fixedColor),
            AnimationColorThemeMode.Random => new SolidColorBrush(BuildRandomColor(appTheme)),
            _ => null
        };
    }

    public static IBrush ResolveTextBrush(IBrush? accentBrush, ThemeMode appTheme)
    {
        if (accentBrush is not null)
            return accentBrush;

        return IsDarkTheme(appTheme) ? Brushes.White : Brushes.Black;
    }

    private static Color BuildRandomColor(ThemeMode appTheme)
    {
        var isDark = IsDarkTheme(appTheme);

        var minValue = isDark ? Math.Min(MinValue * 1.2, 0.85) : Math.Min(MinValue * 0.7, 0.5);
        var maxValue = isDark ? Math.Max(MaxValue, 1.0) : Math.Min(MaxValue * 0.8, 0.7);
        var hue = Random.Shared.NextDouble();
        var saturation = MinSaturation + Random.Shared.NextDouble() * (MaxSaturation - MinSaturation);
        var value = minValue + Random.Shared.NextDouble() * Math.Max(0, maxValue - minValue);
        return FromHsv(hue, saturation, value);
    }

    private static bool IsDarkTheme(ThemeMode appTheme)
    {
        return appTheme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            _ => Application.Current?.ActualThemeVariant == ThemeVariant.Dark
        };
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue * 6 % 2 - 1));
        var m = value - chroma;
        var sector = (int)Math.Floor(hue * 6) % 6;
        (double r, double g, double b) = sector switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    private static byte ToByte(double value)
    {
        return (byte)Math.Clamp((int)Math.Round(value * 255), 0, 255);
    }
}
