using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction;
using SecRandom.Core.ComponentModels;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class PrizeHistory(string name) : ConfigBase
{
    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("data", "history", "lottery_history", name);

    public ObservableDictionary<string, History> Prizes { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<HumanGender, int> GenderStatus { get; set; } = [];

    [ObservableProperty] public partial int TotalRounds { get; set; } = 0;
    [ObservableProperty] public partial int TotalStats { get; set; } = 0;
}