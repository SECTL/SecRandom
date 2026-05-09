using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

public partial class History : ObservableRecipient
{
    [ObservableProperty] private ObservableCollection<HistoryItem> _histories = [];
    [ObservableProperty] private DateTime _lastDrawnTime = DateTime.MinValue;
    [ObservableProperty] private int _roundsMissed;
    [ObservableProperty] private int _totalCount;
}