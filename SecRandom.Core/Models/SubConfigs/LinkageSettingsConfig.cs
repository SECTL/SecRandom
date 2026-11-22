using SecRandom.Core.Enums.Configs;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Models.Linkage;

namespace SecRandom.Core.Models.SubConfigs;

public partial class LinkageSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _verificationRequired = true;
    [ObservableProperty] private bool _instantDrawDisable = false;
    [ObservableProperty] private bool _restrictWhenUnavailable = false;
    [ObservableProperty] private bool _disableOnOutdoorCourse = false;
    [ObservableProperty] private LinkageDataSource _dataSource = LinkageDataSource.Off;
    [ObservableProperty] private bool _hideFloatingWindowOnClassEnd = false;
    [ObservableProperty] private bool _hideFloatingWindowOnOutdoorCourse = false;
    [ObservableProperty] private bool _preClassResetEnabled = false;
    [ObservableProperty] private int _preClassResetTime = 120;
    [ObservableProperty] private int _preClassEnableTime = 0;
    [ObservableProperty] private int _postClassDisableDelay = 0;
    [ObservableProperty] private bool _subjectHistoryFilterEnabled = false;
    [ObservableProperty] private LinkageBreakAssignment _subjectHistoryBreakAssignment = LinkageBreakAssignment.PreviousClass;

    /// <summary>课程联动时按科目选择的点名/闪抽名单和抽奖奖品池。</summary>
    [ObservableProperty] private List<CourseRosterBinding> _courseRosterBindings = [];
}
