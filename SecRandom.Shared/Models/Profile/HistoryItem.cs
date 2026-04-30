using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

public partial class HistoryItem : ObservableRecipient
{
    [ObservableProperty] private DateTime _drawTime = DateTime.Now;
    
    [ObservableProperty] private int _drawMethod = 1;
    [ObservableProperty] private int _drawNumbers = 1;
    [ObservableProperty] private string _drawGroup = string.Empty;
    [ObservableProperty] private string _drawGender = string.Empty;
    
    [ObservableProperty] private double _weight = 1;
}