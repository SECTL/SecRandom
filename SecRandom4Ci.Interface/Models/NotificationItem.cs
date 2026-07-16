using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom4Ci.Interface.Models;

public partial class NotificationItem : ObservableRecipient
{
    [ObservableProperty] private bool _isLottery;
    [ObservableProperty] private string _lotteryName = string.Empty;
    [ObservableProperty] private int _studentId;
    [ObservableProperty] private string _studentName = string.Empty;
    [ObservableProperty] private bool _exists = true;
}
