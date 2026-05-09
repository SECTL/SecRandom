using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class DefaultDrawSettingsConfig : DrawSettingsConfigBase
{
    [ObservableProperty] private ClearRecordMode _clearRecord = ClearRecordMode.Restarted;
    [ObservableProperty] private DrawMode _drawMode = DrawMode.NoRepeat;
    [ObservableProperty] private int _halfRepeat = 1;
}