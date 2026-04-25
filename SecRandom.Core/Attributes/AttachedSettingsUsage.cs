using SecRandom.Core.Enums;

namespace SecRandom.Core.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class AttachedSettingsUsage(AttachedSettingsTargets targets) : Attribute
{
    public AttachedSettingsTargets Targets { get; } = targets;
}