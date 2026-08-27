using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class QuickDrawSettingsConfig : OverridableDrawSettings
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private int _halfRepeat = 1;
    [ObservableProperty] private DrawType _drawType = DrawType.Fair;
    [ObservableProperty] private string _algorithmId = "builtin.fair";
    [ObservableProperty] private string _defaultClass = string.Empty;
    [ObservableProperty] private int _disableAfterClick = 1;
}
