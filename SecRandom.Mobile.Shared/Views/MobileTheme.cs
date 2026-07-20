using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;
using AvaloniaApplication = Avalonia.Application;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 移动端主题入口。颜色 token 的真实值由 Styles/MobileStyles.axaml 的
/// Light/Dark 主题字典持有；页面与工厂应通过 <see cref="Resource(string)"/> /
/// <see cref="BindBrush"/> 建立 DynamicResource 绑定，使主题切换自动刷新。
/// 静态快照画刷属性（Canvas、Text 等）仅保留给外壳（MobileRootView/Header/NavigationBar）
/// 这类已有显式 RefreshTheme 调用的旧代码，页面内容不得再使用。
/// </summary>
internal static class MobileTheme
{
    internal static class Keys
    {
        internal const string Canvas = "MobileCanvasBrush";
        internal const string Surface = "MobileSurfaceBrush";
        internal const string SurfaceMuted = "MobileSurfaceMutedBrush";
        internal const string Border = "MobileBorderBrush";
        internal const string Primary = "MobilePrimaryBrush";
        internal const string PrimaryWash = "MobilePrimaryWashBrush";
        internal const string WarmWash = "MobileWarmWashBrush";
        internal const string MutedText = "MobileMutedTextBrush";
        internal const string Danger = "MobileDangerBrush";
        internal const string Text = "MobileTextBrush";
    }

    internal static DynamicResourceExtension Resource(string key) => new(key);

    internal static IDisposable BindBrush(AvaloniaObject target, AvaloniaProperty property, string key)
        => target.Bind(property, new DynamicResourceExtension(key));

    internal static double FindDouble(string key, double fallback) =>
        AvaloniaApplication.Current is { } app && app.TryFindResource(key, out object? value) && value is double number
            ? number
            : fallback;

    internal static CornerRadius FindCornerRadius(string key, CornerRadius fallback) =>
        AvaloniaApplication.Current is { } app && app.TryFindResource(key, out object? value) && value is CornerRadius radius
            ? radius
            : fallback;

    internal static Thickness FindThickness(string key, Thickness fallback) =>
        AvaloniaApplication.Current is { } app && app.TryFindResource(key, out object? value) && value is Thickness thickness
            ? thickness
            : fallback;

    internal static void Apply(ThemeMode theme)
    {
        AvaloniaApplication.Current!.RequestedThemeVariant = theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    // ---- 以下为旧式静态快照画刷：画刷在取值时按当前主题固化，切主题不会自动更新。 ----
    // ---- 仅限外壳在 RefreshTheme 中显式重取的调用点使用；新代码必须走 Keys + BindBrush。 ----

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

    internal static SolidColorBrush Brush(string light, string? dark = null)
    {
        var color = IsDarkTheme && dark is not null ? dark : light;
        return new SolidColorBrush(Color.Parse(color));
    }

    private static bool IsDarkTheme => AvaloniaApplication.Current?.ActualThemeVariant == ThemeVariant.Dark
                                       || AvaloniaApplication.Current?.RequestedThemeVariant == ThemeVariant.Dark;
}
