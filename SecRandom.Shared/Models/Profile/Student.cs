using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Enums;

namespace SecRandom.Shared.Models.Profile;

public partial class Student : AttachableSettingsObject
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _group = string.Empty;
    [ObservableProperty] private HumanGender _gender = HumanGender.Unknown;
    [ObservableProperty] private bool _exists = true;
}