using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class RollCallSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawType _drawType = DrawType.Random;
    [ObservableProperty] private string _defaultClass = string.Empty;
}