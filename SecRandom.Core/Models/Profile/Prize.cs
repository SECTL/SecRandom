using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.Profile;

public partial class Prize : AttachableSettingsObject
{
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial int Count { get; set; } = 1;
    [ObservableProperty] public partial double Weight { get; set; } = 1;
    [ObservableProperty] public partial bool Exists { get; set; } = true;

    // [ObservableProperty] public partial ObservableCollection<Guid> Tags { get; set; } = [];
}