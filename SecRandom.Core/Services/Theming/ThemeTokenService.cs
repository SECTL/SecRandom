using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using SecRandom.Core.Abstraction.Services.Theming;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Services.Theming;

/// <summary>
///     主题色板：把宿主实际生效的 Fluent 资源键暴露给插件，插件不需要猜 <c>DynamicResource</c> 名字，
///     也不需要自己订阅主题变化。
///     <para>
///         宿主没有自建调色板，色键来自 FluentAvaloniaUI；这里用
///         <c>Application.Current.TryFindResource</c>（会沿逻辑树向上搜）取值，跟随当前主题变体。
///     </para>
/// </summary>
public sealed class ThemeTokenService : IThemeTokenService
{
    /// <summary>宿主界面里最常用的一批色键，<see cref="TokenKeys" /> 返回它们。</summary>
    private static readonly string[] DefaultTokenKeys =
    [
        "SystemAccentColor",
        "AccentFillColorDefaultBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "TextFillColorTertiaryBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
        "SolidBackgroundFillColorBaseBrush",
        "ControlFillColorSecondaryBrush",
        "ControlElevationBorderBrush",
        "ControlStrokeColorDefaultBrush",
        "SystemFillColorCautionBrush",
        "SystemFillColorCriticalBrush",
        "SurfaceStrokeColorDefaultBrush",
        "ControlCornerRadius"
    ];

    private readonly IUiStyleService? _styleService;

    public ThemeTokenService(IUiStyleService? styleService = null)
    {
        _styleService = styleService;
        if (_styleService is not null)
            _styleService.Changed += OnStyleChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public ThemeKind CurrentTheme => IsDark ? ThemeKind.Dark : ThemeKind.Light;

    /// <inheritdoc />
    public bool IsDark => ResolveVariant() == ThemeVariant.Dark;

    /// <inheritdoc />
    public IReadOnlyList<string> TokenKeys => DefaultTokenKeys;

    /// <inheritdoc />
    public bool TryGetColor(string tokenKey, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(tokenKey) || !TryGetResource(tokenKey, out var value))
            return false;

        switch (value)
        {
            case Color direct:
                color = direct;
                return true;
            case ISolidColorBrush solid:
                color = solid.Color;
                return true;
            default:
                return false;
        }
    }

    /// <inheritdoc />
    public Color GetColor(string tokenKey, Color fallback) =>
        TryGetColor(tokenKey, out var color) ? color : fallback;

    /// <inheritdoc />
    public object? GetResource(string resourceKey) =>
        TryGetResource(resourceKey, out var value) ? value : null;

    /// <inheritdoc />
    public bool TryGetResource(string resourceKey, out object? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(resourceKey))
            return false;

        var application = Application.Current;
        if (application is null)
            return false;

        try
        {
            return application.TryFindResource(resourceKey, out value) && value is not null;
        }
        catch (Exception)
        {
            // 资源系统在应用退出/换主题的瞬间可能抛，插件不该因为读个颜色崩掉。
            value = null;
            return false;
        }
    }

    private void OnStyleChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    private static ThemeVariant ResolveVariant()
    {
        var application = Application.Current;
        var variant = application?.ActualThemeVariant;
        if (variant == ThemeVariant.Dark)
            return ThemeVariant.Dark;
        if (variant == ThemeVariant.Light)
            return ThemeVariant.Light;

        // RequestedThemeVariant 是 Default 时按系统偏好兜底。
        var platform = application?.PlatformSettings?.GetColorValues().ThemeVariant;
        return platform == PlatformThemeVariant.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
