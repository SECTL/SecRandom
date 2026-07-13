using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

public partial class HistoryItem : ObservableRecipient
{
    [ObservableProperty] private string _recordId = string.Empty;
    [ObservableProperty] private string _recordNumber = string.Empty;
    [ObservableProperty] private string _recordName = string.Empty;
    [ObservableProperty] private string _recordGender = string.Empty;
    [ObservableProperty] private string _recordGroup = string.Empty;

    [ObservableProperty] private string _drawGender = string.Empty;
    [ObservableProperty] private string _drawGroup = string.Empty;

    [ObservableProperty] private int _drawMethod = 1;
    [ObservableProperty] private int _drawNumbers = 1;
    [ObservableProperty] private DateTime _drawTime = DateTime.Now;
    // Entries from the same selection share one ID so external history queries can group them exactly.
    [ObservableProperty] private string _drawRoundId = string.Empty;

    [ObservableProperty] private double _weight = 1;
}
