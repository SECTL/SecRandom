using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Helpers;

internal static class NotificationBackgroundBrushes
{
    public static Color ResolveThemeSurfaceColor(ThemeVariant variant)
    {
        foreach (var key in new[] { "SolidBackgroundFillColorBaseBrush", "SolidBackgroundFillColorBase",
                     "ApplicationBackgroundBrush", "SystemControlBackgroundAltHighBrush" })
        {
            if (Application.Current?.TryFindResource(key, variant, out var value) != true)
                continue;
            if (value is Color { A: 255 } color)
                return color;
            if (value is ISolidColorBrush { Color.A: 255, Opacity: 1 } brush)
                return brush.Color;
        }
        return variant == ThemeVariant.Light ? Color.FromRgb(0xF3, 0xF3, 0xF3) : Color.FromRgb(0x20, 0x20, 0x20);
    }

    public static IBrush Create(NotificationChannelSettings settings, Color themeColor) => settings.BackgroundStyle switch
    {
        WindowBackgroundStyle.Theme => new SolidColorBrush(themeColor, Math.Clamp(settings.ThemeBackgroundOpacity, 0, 100) / 100.0),
        WindowBackgroundStyle.Solid => new SolidColorBrush(settings.BackgroundColor),
        WindowBackgroundStyle.Gradient => CreateGradient(settings.BackgroundGradientStart, settings.BackgroundGradientEnd, settings.BackgroundGradientAngle),
        _ => new SolidColorBrush(themeColor, Math.Clamp(settings.ThemeBackgroundOpacity, 0, 100) / 100.0)
    };

    public static LinearGradientBrush CreateGradient(Color start, Color end, int angle)
    {
        var radians = Math.Clamp(angle, 0, 360) * Math.PI / 180;
        var dx = Math.Cos(radians) * 0.5;
        var dy = Math.Sin(radians) * 0.5;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(start, 0), new(end, 1) }
        };
    }
}
