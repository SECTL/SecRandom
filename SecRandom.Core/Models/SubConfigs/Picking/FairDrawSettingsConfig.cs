using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs.Picking;

public partial class FairDrawSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _fairDraw = true;
}
