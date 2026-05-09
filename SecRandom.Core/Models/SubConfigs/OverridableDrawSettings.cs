using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class OverridableDrawSettings : DrawSettingsConfigBase
{
    [ObservableProperty] private bool _overrideAnimationSettings;
    [ObservableProperty] private bool _overrideColorSettings;
    [ObservableProperty] private bool _overrideDisplaySettings;
    [ObservableProperty] private bool _overrideMusicSettings;
    [ObservableProperty] private bool _overrideStudentImageSettings;
}