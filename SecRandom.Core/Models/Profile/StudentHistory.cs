using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Abstraction;
using SecRandom.Core.ComponentModels;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class StudentHistory(string name) : ConfigBase
{
    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("data", "history", "roll_call_history", name);
    
    public ObservableDictionary<string, History> Students { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<HumanGender, int> GenderStatus { get; set; } = [];

    [ObservableProperty] private int _totalRounds = 0;
    [ObservableProperty] private int _totalStats = 0;
}