using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     一条悬浮窗自定义按钮配置。
/// </summary>
/// <remarks>
///     这是会被序列化进 <c>settings.json</c> 的纯数据对象：属性必须保持可读写、默认值安全，
///     新增字段只能追加。行级变更通知由设置页的行模型负责，这里不实现 <c>INotifyPropertyChanged</c>。
/// </remarks>
public sealed class FloatingWindowButtonConfig
{
    private const int MaxStoredImageLength = 128;

    /// <summary>稳定标识，用于表格行身份与排序；不展示给用户。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public bool Enabled { get; set; } = true;

    /// <summary>按钮文字；纯图标样式下仍用于工具提示、表格和辅助功能。</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>内置 Fluent 图标字形（来自生成的 <c>FluentIcons</c> 常量）。</summary>
    public string IconGlyph { get; set; } = string.Empty;

    /// <summary>
    ///     自定义图标文件名（位于应用管理的图标目录下）。为空表示使用 <see cref="IconGlyph" />。
    ///     只存文件名、不存绝对路径，配置才能随备份迁移到另一台机器。
    /// </summary>
    public string IconImage { get; set; } = string.Empty;

    public FloatingWindowButtonActionKind ActionKind { get; set; }

    /// <summary>动作参数：主页面 ID / 设置页 ID / URL / 本地路径；无参动作留空。</summary>
    public string ActionTarget { get; set; } = string.Empty;

    /// <summary>
    ///     复制出一条独立配置（用于"复制按钮"）。<see cref="Id" /> 会重新生成，
    ///     <see cref="IconImage" /> 也被清空，避免两行引用同一份需要独立清理的图标文件。
    /// </summary>
    public FloatingWindowButtonConfig CloneAsNew()
    {
        return new FloatingWindowButtonConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Enabled = Enabled,
            Label = Label.Length > 6 ? string.Concat(Label.AsSpan(0, 6), "副本") : $"{Label}副本",
            IconGlyph = IconGlyph,
            IconImage = string.Empty,
            ActionKind = ActionKind,
            ActionTarget = ActionTarget
        };
    }

    internal static bool IsSafeImageName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxStoredImageLength)
            return false;
        if (!string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal))
            return false;

        return value.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
    }
}
