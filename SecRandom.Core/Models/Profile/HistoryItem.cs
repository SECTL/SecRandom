using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class HistoryItem : ObservableRecipient
{
    [ObservableProperty] public partial DateTime DrawTime { get; set; } = DateTime.Now;
    
    [ObservableProperty] public partial int DrawMethod { get; set; } = 1;
    [ObservableProperty] public partial int DrawNumbers { get; set; } = 1;
    [ObservableProperty] public partial string DrawGroup { get; set; } = string.Empty;
    [ObservableProperty] public partial HumanGender DrawGender { get; set; } = HumanGender.None;
    
    [ObservableProperty] public partial double Weight { get; set; } = 1;
}