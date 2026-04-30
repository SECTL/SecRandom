using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class QuickDrawSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawType _drawType = DrawType.Random;
    [ObservableProperty] private string _defaultClass = string.Empty;
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private int _disableAfterClick = 1;
}