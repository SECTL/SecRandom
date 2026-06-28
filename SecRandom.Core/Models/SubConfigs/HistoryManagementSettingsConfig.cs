using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class HistoryManagementSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _showRollCallHistory = true;
    [ObservableProperty] private bool _showLotteryHistory = true;
    [ObservableProperty] private bool _selectWeight = false;
    [ObservableProperty] private string _selectedClassName = string.Empty;
    [ObservableProperty] private string _selectedPoolName = string.Empty;
}
