using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.Profile;

public partial class History : ObservableRecipient
{
    [ObservableProperty] public partial int TotalCount { get; set; } = 0;
    [ObservableProperty] public partial DateTime LastDrawnTime { get; set; } = DateTime.MinValue;
    [ObservableProperty] public partial int RoundsMissed { get; set; } = 0;
    [ObservableProperty] public partial ObservableCollection<HistoryItem> history { get; set; } = [];
}