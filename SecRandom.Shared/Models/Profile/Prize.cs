using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

public partial class Prize : AttachableSettingsObject
{
    [ObservableProperty] private int _count = 1;
    [ObservableProperty] private bool _exists = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private double _weight = 1;
}