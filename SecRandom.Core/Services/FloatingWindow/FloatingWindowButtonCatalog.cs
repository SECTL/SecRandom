using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Services.FloatingWindow;

/// <summary>
///     悬浮窗自定义按钮的展示规则与配置校验。渲染、设置页、动作执行共用这里的判断，
///     避免"配置页允许保存"和"悬浮窗实际画不出来"两套标准。
/// </summary>
public static class FloatingWindowButtonCatalog
{
    public const int MaxButtons = 8;
    public const int MaxLabelLength = 8;
    public const int MaxGlyphLength = 16;
    public const int MaxUriLength = 2048;
    public const int MaxPathLength = 1024;

    /// <summary>
    ///     选取真正要画到悬浮窗上的按钮：启用、名称非空、有图标，且最多 <see cref="MaxButtons" /> 个。
    ///     超出的条目仍留在配置里，不会因为一次渲染就被删除。
    /// </summary>
    public static IReadOnlyList<FloatingWindowButtonConfig> GetRenderableButtons(
        IEnumerable<FloatingWindowButtonConfig>? buttons)
    {
        if (buttons is null)
            return [];

        var result = new List<FloatingWindowButtonConfig>(MaxButtons);
        foreach (var button in buttons)
        {
            if (result.Count >= MaxButtons)
                break;
            if (button is null || !button.Enabled)
                continue;
            if (!TryGetDisplayLabel(button.Label, out _))
                continue;
            if (!HasIcon(button))
                continue;
            if (button.ActionKind == FloatingWindowButtonActionKind.MainPage &&
                string.IsNullOrWhiteSpace(button.ActionTarget))
                continue;

            result.Add(button);
        }

        return result;
    }

    public static bool TryGetDisplayLabel(string? label, out string text)
    {
        text = label?.Trim() ?? string.Empty;
        return text.Length > 0;
    }

    public static bool HasIcon(FloatingWindowButtonConfig button)
    {
        return !string.IsNullOrWhiteSpace(button.IconGlyph) || IsSafeImageName(button.IconImage);
    }

    public static bool IsSafeImageName(string? value)
    {
        return FloatingWindowButtonConfig.IsSafeImageName(value);
    }

    /// <summary>
    ///     校验一条配置能否保存。返回的错误与本地化资源解耦，设置页负责翻译。
    /// </summary>
    public static FloatingWindowButtonValidationError Validate(FloatingWindowButtonConfig button)
    {
        if (button is null)
            return FloatingWindowButtonValidationError.UnsupportedAction;

        if (!TryGetDisplayLabel(button.Label, out var label))
            return FloatingWindowButtonValidationError.LabelRequired;
        if (label.Length > MaxLabelLength)
            return FloatingWindowButtonValidationError.LabelTooLong;

        if (!string.IsNullOrWhiteSpace(button.IconImage) && !IsSafeImageName(button.IconImage))
            return FloatingWindowButtonValidationError.IconImageInvalid;
        if (string.IsNullOrWhiteSpace(button.IconGlyph) && string.IsNullOrWhiteSpace(button.IconImage))
            return FloatingWindowButtonValidationError.IconMissing;

        return button.ActionKind switch
        {
            FloatingWindowButtonActionKind.MainPage => ValidateTarget(button.ActionTarget),
            FloatingWindowButtonActionKind.QuickDraw => FloatingWindowButtonValidationError.None,
            FloatingWindowButtonActionKind.Timer => FloatingWindowButtonValidationError.None,
            FloatingWindowButtonActionKind.SettingsPage => ValidateTarget(button.ActionTarget),
            FloatingWindowButtonActionKind.Uri => ValidateUri(button.ActionTarget),
            FloatingWindowButtonActionKind.Path => ValidatePath(button.ActionTarget),
            _ => FloatingWindowButtonValidationError.UnsupportedAction
        };
    }

    public static string GetDefaultTarget(FloatingWindowButtonActionKind kind)
    {
        return kind switch
        {
            FloatingWindowButtonActionKind.MainPage => "main.rollCall",
            FloatingWindowButtonActionKind.SettingsPage => "settings.overview",
            _ => string.Empty
        };
    }

    public static string GetSuggestedIcon(FloatingWindowButtonActionKind kind)
    {
        return kind switch
        {
            FloatingWindowButtonActionKind.MainPage => FluentIcons.PeopleFilled,
            FloatingWindowButtonActionKind.QuickDraw => FluentIcons.FlashFilled,
            FloatingWindowButtonActionKind.Timer => FluentIcons.TimerFilled,
            FloatingWindowButtonActionKind.SettingsPage => FluentIcons.SettingsFilled,
            FloatingWindowButtonActionKind.Uri => FluentIcons.LinkFilled,
            FloatingWindowButtonActionKind.Path => FluentIcons.FolderFilled,
            _ => FluentIcons.StarFilled
        };
    }

    private static FloatingWindowButtonValidationError ValidateTarget(string? target)
    {
        return string.IsNullOrWhiteSpace(target)
            ? FloatingWindowButtonValidationError.TargetRequired
            : FloatingWindowButtonValidationError.None;
    }

    private static FloatingWindowButtonValidationError ValidateUri(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return FloatingWindowButtonValidationError.TargetRequired;
        if (target.Length > MaxUriLength)
            return FloatingWindowButtonValidationError.TargetTooLong;

        return Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? FloatingWindowButtonValidationError.None
            : FloatingWindowButtonValidationError.TargetInvalidUri;
    }

    private static FloatingWindowButtonValidationError ValidatePath(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return FloatingWindowButtonValidationError.TargetRequired;
        if (target.Length > MaxPathLength)
            return FloatingWindowButtonValidationError.TargetTooLong;

        return Path.IsPathFullyQualified(target)
            ? FloatingWindowButtonValidationError.None
            : FloatingWindowButtonValidationError.TargetInvalidPath;
    }
}
