using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class OverridableDrawSettings : DrawSettingsConfigBase
{
    [ObservableProperty] private bool _overrideDisplaySettings = false;
    [ObservableProperty] private bool _overrideAnimationSettings = false;
    [ObservableProperty] private bool _overrideColorSettings = false;
    [ObservableProperty] private bool _overrideStudentImageSettings = false;
    [ObservableProperty] private bool _overrideMusicSettings = false;
}