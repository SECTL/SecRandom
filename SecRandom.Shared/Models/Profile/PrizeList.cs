using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Shared.Models.Profile;

public class PrizeList : ProfileConfigBase, IAttachableSettingsObject
{
    public PrizeList()
    {
    }

    public PrizeList(string name)
    {
        Name = name;
    }

    [JsonIgnore] public sealed override string Name { get; set; } = string.Empty;

    [JsonIgnore]
    public override string ConfigFilePath =>
        Utils.GetFilePath("data", "list", "lottery_list", $"{Name}.json");

    public ObservableCollection<Prize> Prizes { get; set; } = [];

    public Dictionary<Guid, object?> AttachedObjects { get; set; } = [];
}