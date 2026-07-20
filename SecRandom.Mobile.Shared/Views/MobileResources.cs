using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Styling;
using SecRandom.Core.Enums.Configs;
using AvaloniaApplication = Avalonia.Application;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 移动端主题入口。颜色 token 的真实值由 Styles/MobileStyles.axaml 的
/// Light/Dark 主题字典持有；页面与工厂应通过 <see cref="Resource(string)"/> /
/// <see cref="BindBrush"/> 建立 DynamicResource 绑定，使主题切换自动刷新。
/// 页面和控件通过键或绑定引用这些资源，避免固化主题画刷快照。
/// </summary>
internal static class MobileResources
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

}
