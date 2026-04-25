using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.ComponentModels;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class StudentHistory : ObservableRecipient
{
    public ObservableDictionary<string, History> Students { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<HumanGender, int> GenderStatus { get; set; } = [];

    [ObservableProperty] public partial int TotalRounds { get; set; } = 0;
    [ObservableProperty] public partial int TotalStats { get; set; } = 0;
}