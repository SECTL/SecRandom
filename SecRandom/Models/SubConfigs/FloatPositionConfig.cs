using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Models.SubConfigs;

public partial class FloatPositionConfig : ObservableObject
{
    [ObservableProperty] private int _x = 100;
    [ObservableProperty] private int _y = 100;
}