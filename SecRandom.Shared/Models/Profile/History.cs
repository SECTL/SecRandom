using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

public partial class History : ObservableRecipient
{
    [ObservableProperty] private int _totalCount = 0;
    [ObservableProperty] private DateTime _lastDrawnTime = DateTime.MinValue;
    [ObservableProperty] private int _roundsMissed = 0;
    [ObservableProperty] private ObservableCollection<HistoryItem> _histories = [];
}