using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class LinkageSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _verificationRequired = true;
    [ObservableProperty] private bool _instantDrawDisable = false;
    [ObservableProperty] private int _dataSource = 0;
    [ObservableProperty] private bool _hideFloatingWindowOnClassEnd = false;
    [ObservableProperty] private bool _preClassResetEnabled = false;
    [ObservableProperty] private int _preClassResetTime = 120;
    [ObservableProperty] private int _preClassEnableTime = 0;
    [ObservableProperty] private int _postClassDisableDelay = 0;
    [ObservableProperty] private bool _subjectHistoryFilterEnabled = false;
    [ObservableProperty] private int _subjectHistoryBreakAssignment = 1;
}
