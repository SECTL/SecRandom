using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class MoreSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _backgroundMusicLoop = true;
    [ObservableProperty] private RollCallControlPanelPosition _rollCallControlPanelPosition = RollCallControlPanelPosition.Right;
    [ObservableProperty] private bool _rollCallResetButton = true;
    [ObservableProperty] private bool _rollCallQuantityControl = true;
    [ObservableProperty] private bool _rollCallStartButton = true;
    [ObservableProperty] private bool _rollCallListSelector = true;
    [ObservableProperty] private bool _rollCallRangeSelector = true;
    [ObservableProperty] private bool _rollCallGenderSelector = true;
    [ObservableProperty] private bool _rollCallRemainingButton = true;
    [ObservableProperty] private bool _rollCallQuantityLabel = true;
    [ObservableProperty] private RollCallControlPanelPosition _lotteryControlPanelPosition = RollCallControlPanelPosition.Right;
    [ObservableProperty] private bool _lotteryResetButton = true;
    [ObservableProperty] private bool _lotteryQuantityControl = true;
    [ObservableProperty] private bool _lotteryStartButton = true;
    [ObservableProperty] private bool _lotteryListSelector = true;
    [ObservableProperty] private bool _lotteryStudentListSelector = true;
    [ObservableProperty] private bool _lotteryRangeSelector = true;
    [ObservableProperty] private bool _lotteryGenderSelector = true;
    [ObservableProperty] private bool _lotteryRemainingButton = true;
    [ObservableProperty] private bool _lotteryQuantityLabel = true;
    [ObservableProperty] private bool _enableShortcut = false;
    [ObservableProperty] private string _openRollCallPageShortcut = string.Empty;
    [ObservableProperty] private string _quickDrawShortcut = string.Empty;
    [ObservableProperty] private string _openLotteryPageShortcut = string.Empty;
}
