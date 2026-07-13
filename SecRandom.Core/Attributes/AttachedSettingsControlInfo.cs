using SecRandom.Core.Enums;
using SecRandom.Core.Icons;

namespace SecRandom.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AttachedSettingsControlInfo(
    string guid,
    string iconGlyph = FluentIcons.SettingsFilled,
    bool hasEnabledState = true) : Attribute
{
    public Guid Guid { get; } = Guid.Parse(guid);
    public Type AttachedSettingsControlType { get; internal set; } = null!;

    public string Name { get; internal set; } = string.Empty;
    public string IconGlyph { get; } = iconGlyph;
    public bool HasEnabledState { get; } = hasEnabledState;
    public AttachedSettingsTargets Targets { get; internal set; } = AttachedSettingsTargets.None;
}
