using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;
using AvaloniaApplication = Avalonia.Application;

namespace SecRandom.Mobile.Views;

internal static class MobileTheme
{
    internal static SolidColorBrush Canvas => Brush("#F7F8FA", "#111817");
    internal static SolidColorBrush Surface => Brush("#FFFFFF", "#1C2523");
    internal static SolidColorBrush SurfaceMuted => Brush("#E7ECEB", "#27312F");
    internal static SolidColorBrush Border => Brush("#E4E7EC", "#35413E");
    internal static SolidColorBrush Primary => Brush("#006E5B", "#72D8C2");
    internal static SolidColorBrush PrimaryWash => Brush("#E8F4F0", "#173D35");
    internal static SolidColorBrush WarmWash => Brush("#FFF4E5", "#402E18");
    internal static SolidColorBrush MutedText => Brush("#667085", "#A5B0AD");
    internal static SolidColorBrush Danger => Brush("#B42318", "#FFB4AB");
    internal static SolidColorBrush Text => Brush("#1D2939", "#E7ECEB");

    internal static void Apply(ThemeMode theme)
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    internal static SolidColorBrush Brush(string light, string? dark = null)
    {
        var color = IsDarkTheme && dark is not null ? dark : light;
        return new SolidColorBrush(Color.Parse(color));
    }

    private static bool IsDarkTheme => AvaloniaApplication.Current?.ActualThemeVariant == ThemeVariant.Dark
                                       || AvaloniaApplication.Current?.RequestedThemeVariant == ThemeVariant.Dark;
}
