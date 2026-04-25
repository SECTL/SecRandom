using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class Student : AttachableSettingsObject
{
    [ObservableProperty] public partial string Id { get; set; } = string.Empty;
    [ObservableProperty] public partial string Name { get; set; } = string.Empty;
    [ObservableProperty] public partial string Group { get; set; } = string.Empty;
    [ObservableProperty] public partial HumanGender Gender { get; set; } = HumanGender.Unknown;
    [ObservableProperty] public partial bool Exists { get; set; } = true;
    
    // [ObservableProperty] public partial ObservableCollection<Guid> Tags { get; set; } = [];
}