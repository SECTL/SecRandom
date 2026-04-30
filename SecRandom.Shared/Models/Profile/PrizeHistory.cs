using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.ComponentModels;

namespace SecRandom.Shared.Models.Profile;

public partial class PrizeHistory : ProfileConfigBase
{
    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;
    [JsonIgnore] public override string ConfigFilePath =>
        Utils.GetFilePath("data", "history", "lottery_history", $"{Name}.json");
    
    public ObservableDictionary<string, History> Prizes { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<string, int> GenderStatus { get; set; } = [];

    [ObservableProperty] private int _totalRounds = 0;
    [ObservableProperty] private int _totalStats = 0;

    public PrizeHistory() { }
    
    public PrizeHistory(string name)
    {
        Name = name;
    }
}