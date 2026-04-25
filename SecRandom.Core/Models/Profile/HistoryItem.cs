using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Models.Profile;

public partial class HistoryItem : ObservableRecipient
{
    [ObservableProperty] private DateTime _drawTime = DateTime.Now;
    
    [ObservableProperty] private int _drawMethod = 1;
    [ObservableProperty] private int _drawNumbers = 1;
    [ObservableProperty] private string _drawGroup = string.Empty;
    [ObservableProperty] private HumanGender _drawGender = HumanGender.None;
    
    [ObservableProperty] private double _weight = 1;
}