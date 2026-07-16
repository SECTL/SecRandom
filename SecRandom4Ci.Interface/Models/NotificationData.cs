using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom4Ci.Interface.Enums;

namespace SecRandom4Ci.Interface.Models;

public partial class NotificationData : ObservableRecipient
{
    [ObservableProperty] private string _className = string.Empty;
    [ObservableProperty] private List<NotificationItem> _items = [];
    [ObservableProperty] private int _drawCount = 1;
    [ObservableProperty] private double _displayDuration = 5.0;
    [ObservableProperty] private bool _animation = true;
    [ObservableProperty] private ResultType _resultType = ResultType.Unknown;
}
