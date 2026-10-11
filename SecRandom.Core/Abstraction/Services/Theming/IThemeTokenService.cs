using Avalonia.Media;

namespace SecRandom.Core.Abstraction.Services.Theming;

/// <summary>当前主题。</summary>
public enum ThemeKind
{
    /// <summary>浅色。</summary>
    Light = 0,

    /// <summary>深色。</summary>
    Dark = 1
}

/// <summary>
///     主题色板：插件用它读宿主**当前实际**的颜色与资源，而不是硬编码 <c>DynamicResource</c> 的键名。
/// </summary>
public interface IThemeTokenService
{
    /// <summary>当前主题。</summary>
    ThemeKind CurrentTheme { get; }

    /// <summary>当前是否深色。</summary>
    bool IsDark { get; }

    /// <summary>主题切换（可能与语言/系统设置一起变）。</summary>
    event EventHandler? Changed;

    /// <summary>按语义键取颜色（键名见 <see cref="TokenKeys" />）。</summary>
    bool TryGetColor(string tokenKey, out Color color);

    /// <summary>按语义键取颜色，取不到时用 <paramref name="fallback" />。</summary>
    Color GetColor(string tokenKey, Color fallback);

    /// <summary>按宿主资源键（如 <c>SystemAccentColor</c>）取任意资源。</summary>
    object? GetResource(string resourceKey);

    /// <summary>按宿主资源键取任意资源，带回是否成功。</summary>
    bool TryGetResource(string resourceKey, out object? value);

    /// <summary>宿主当前可用的语义色键名。</summary>
    IReadOnlyList<string> TokenKeys { get; }
}
