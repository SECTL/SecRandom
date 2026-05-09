using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.ComponentModels;

namespace SecRandom.Shared.Models.Profile;

public partial class StudentHistory : ProfileConfigBase
{
    [ObservableProperty] private int _totalRounds;
    [ObservableProperty] private int _totalStats;

    public StudentHistory()
    {
    }

    public StudentHistory(string name)
    {
        Name = name;
    }

    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath("data", "history", "roll_call_history", $"{Name}.json");

    public ObservableDictionary<string, History> Students { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<string, int> GenderStatus { get; set; } = [];
}