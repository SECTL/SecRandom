using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class RollCallSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private string _defaultClass = string.Empty;
    [ObservableProperty] private DrawType _drawType = DrawType.Random;
}