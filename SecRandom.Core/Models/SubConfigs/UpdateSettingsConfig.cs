using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class UpdateSettingsConfig : ObservableObject
{
    [ObservableProperty] private int _autoUpdateMode = 3;
    [ObservableProperty] private int _updateChannel = 0;
    [ObservableProperty] private int _updateSource = 0;
    [ObservableProperty] private DateTime _lastCheckTime = new(1970, 1, 1, 8, 0, 0, DateTimeKind.Local);
}
