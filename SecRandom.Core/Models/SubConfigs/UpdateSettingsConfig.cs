using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Updates;

namespace SecRandom.Core.Models.SubConfigs;

public partial class UpdateSettingsConfig : ObservableObject
{
    [ObservableProperty] private int _autoUpdateMode = 3;
    [ObservableProperty] private UpdateChannel _updateChannel = UpdateChannel.Release;
    [ObservableProperty] private UpdateSource _updateSource = UpdateSource.GitHub;
    [ObservableProperty] private DateTime _lastCheckTime = new(1970, 1, 1, 8, 0, 0, DateTimeKind.Local);
}
