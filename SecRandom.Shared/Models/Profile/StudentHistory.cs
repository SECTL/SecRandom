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
        GetProfileFilePath("history", "roll_call_history");

    public ObservableDictionary<string, History> Students { get; set; } = [];
    public ObservableDictionary<string, int> GroupStats { get; set; } = [];
    public ObservableDictionary<string, int> GenderStatus { get; set; } = [];

    /// <summary>
    ///     Registered history groups of this profile. Empty group names are the implicit default group and are never
    ///     listed. Counts are derived from <see cref="Students"/> records, never stored here.
    /// </summary>
    public ObservableDictionary<string, HistoryGroupInfo> HistoryGroups { get; set; } = [];
}
