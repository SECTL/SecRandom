using SecRandom.Core.Enums;

namespace SecRandom.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AttachedSettingsControlInfo(
    string guid,
    string name,
    string iconGlyph = "\uef27",
    bool hasEnabledState = true) : Attribute
{
    public Guid Guid { get; } = Guid.Parse(guid);
    public Type AttachedSettingsControlType { get; internal set; } = null!;

    public string Name { get; } = name;
    public string IconGlyph { get; } = iconGlyph;
    public bool HasEnabledState { get; } = hasEnabledState;
    public AttachedSettingsTargets Targets { get; internal set; } = AttachedSettingsTargets.None;
}