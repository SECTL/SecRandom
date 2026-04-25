using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using SecRandom.Core.Abstraction;

namespace SecRandom.Core.Models.Profile;

public class PrizeList(string name) : ConfigBase
{
    [JsonIgnore] public override string ConfigFilePath => Utils.GetFilePath("data", "list", "lottery_list", name);
    
    public ObservableCollection<Prize> Prizes { get; set; } = [];
}