using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.ComponentModels;
using SecRandom.Shared.Enums;

namespace SecRandom.Shared.Models.Profile;

public partial class StudentHistory : ProfileConfigBase
{
    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;
    [JsonIgnore] public override string ConfigFilePath =>
        Utils.GetFilePath("data", "history", "roll_call_history", $"{Name}.json");
    
    public ObservableDictionary<string, History> Students { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<HumanGender, int> GenderStatus { get; set; } = [];

    [ObservableProperty] private int _totalRounds = 0;
    [ObservableProperty] private int _totalStats = 0;
    
    public StudentHistory() { }
    
    public StudentHistory(string name)
    {
        Name = name;
    }
}