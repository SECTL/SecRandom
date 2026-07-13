using SecRandom.Core.Icons;

namespace SecRandom.Core.MarkupExtensions;

/// <summary>
///     XAML 标记扩展，用法：<c>{ci:FI AccessTimeFilled}</c>
/// </summary>
public class FiExtension
{
    /// <inheritdoc cref="FiExtension" />
    public FiExtension()
    {
    }

    /// <inheritdoc cref="FiExtension" />
    public FiExtension(FluentIconKind icon)
    {
        Icon = icon;
    }

    /// <summary>
    ///     Fluent Icon 种类
    /// </summary>
    public FluentIconKind Icon { get; set; }

    /// <summary>
    ///     提供值
    /// </summary>
    /// <param name="serviceProvider">Avalonia 服务提供器</param>
    /// <returns>Fluent Icon 字符串值</returns>
    public string ProvideValue(IServiceProvider serviceProvider)
    {
        return char.ConvertFromUtf32((int)Icon);
    }
}