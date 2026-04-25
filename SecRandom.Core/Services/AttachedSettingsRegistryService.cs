using System.Collections.ObjectModel;
using SecRandom.Core.Attributes;

namespace SecRandom.Core.Services;

public static class AttachedSettingsRegistryService
{
    public static ObservableCollection<AttachedSettingsControlInfo> RegisteredControls { get; } = [];
    public static ObservableCollection<AttachedSettingsControlInfo> StudentAttachedSettingsControls { get; } = [];
    public static ObservableCollection<AttachedSettingsControlInfo> PrizeAttachedSettingsControls { get; } = [];
}