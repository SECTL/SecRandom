using System;

namespace SecRandom.Models;

/// <summary>
///     单选设置项：ComboBox 用 <see cref="Label" /> 展示，页面按 <see cref="Value" /> 写回配置。
/// </summary>
public sealed class SettingOption<T>(T value, string label) where T : struct, Enum
{
    public T Value { get; } = value;
    public string Label { get; } = label;

    public override string ToString() => Label;
}
