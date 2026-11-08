using Avalonia;
using Avalonia.Media;

namespace SecRandom.Helpers;

/// <summary>
///     解析应用当前的主题强调色，供"跟随主题"的全屏外观使用。
/// </summary>
/// <remarks>
///     FluentAvalonia 把系统强调色投影成 <c>SystemAccentColor</c> 资源，所以优先读资源；
///     读不到时退回平台颜色值，最后才用 Fluent 的默认强调蓝，保证任何运行时都能拿到一个可绘制颜色。
/// </remarks>
internal static class ThemeAccentColor
{
    private static readonly Color Fallback = Color.FromRgb(0x0F, 0x6C, 0xBD);

    /// <summary>当前主题的强调色。</summary>
    public static Color Resolve()
    {
        var application = Application.Current;
        if (application is null)
            return Fallback;

        var theme = application.ActualThemeVariant;
        foreach (var key in ResourceKeys)
        {
            if (!application.TryGetResource(key, theme, out var value))
                continue;

            switch (value)
            {
                case Color color:
                    return color;
                case ISolidColorBrush brush:
                    return brush.Color;
            }
        }

        try
        {
            if (application.PlatformSettings is { } platformSettings)
                return platformSettings.GetColorValues().AccentColor1;
        }
        catch (Exception)
        {
            // 平台颜色值只是兜底手段：取不到就继续用默认强调色。
        }

        return Fallback;
    }

    private static readonly string[] ResourceKeys =
    [
        "SystemAccentColor",
        "SystemAccentColorLight1",
        "SystemControlHighlightAccentBrush"
    ];
}
