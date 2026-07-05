using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class DefaultDrawSettingsConfig : DrawSettingsConfigBase
{
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private int _halfRepeat = 1;
}
