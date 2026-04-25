using System.Collections.ObjectModel;

namespace SecRandom.Core.Models.Profile;

public class PrizeList
{
    public ObservableCollection<Prize> Prizes { get; set; } = [];
}