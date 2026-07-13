using System.Globalization;
using System.Resources;

namespace SecRandom.Langs.Ipc;

public static class Resources
{
    private static readonly ResourceManager Manager = new("SecRandom.Langs.Ipc.Resources", typeof(Resources).Assembly);
    private static string Text(string key) => Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string M_Disabled => Text(nameof(M_Disabled));
    public static string M_DataOnlyIpc => Text(nameof(M_DataOnlyIpc));
    public static string M_UnsupportedCommand => Text(nameof(M_UnsupportedCommand));
    public static string M_InvalidMainPage => Text(nameof(M_InvalidMainPage));
    public static string M_InvalidSettingsPage => Text(nameof(M_InvalidSettingsPage));
    public static string M_InvalidWindowAction => Text(nameof(M_InvalidWindowAction));
    public static string M_MainWindowRequested => Text(nameof(M_MainWindowRequested));
    public static string M_SettingsHidden => Text(nameof(M_SettingsHidden));
    public static string M_SettingsPreviewOpened => Text(nameof(M_SettingsPreviewOpened));
    public static string M_SettingsRequested => Text(nameof(M_SettingsRequested));
    public static string M_FloatPageUnsupported => Text(nameof(M_FloatPageUnsupported));
    public static string M_FloatingRequested => Text(nameof(M_FloatingRequested));
    public static string M_MainToggled => Text(nameof(M_MainToggled));
    public static string M_FloatingToggled => Text(nameof(M_FloatingToggled));
    public static string M_Restarting => Text(nameof(M_Restarting));
    public static string M_Exiting => Text(nameof(M_Exiting));
    public static string M_RollCallStarted => Text(nameof(M_RollCallStarted));
    public static string M_RollCallStopped => Text(nameof(M_RollCallStopped));
    public static string M_RollCallReset => Text(nameof(M_RollCallReset));
    public static string M_QuickDrawSucceeded => Text(nameof(M_QuickDrawSucceeded));
    public static string M_LotteryStarted => Text(nameof(M_LotteryStarted));
    public static string M_LotteryStopped => Text(nameof(M_LotteryStopped));
    public static string M_LotteryReset => Text(nameof(M_LotteryReset));
    public static string M_RollCallCountSet => Text(nameof(M_RollCallCountSet));
    public static string M_RollCallGroupSet => Text(nameof(M_RollCallGroupSet));
    public static string M_RollCallGenderSet => Text(nameof(M_RollCallGenderSet));
    public static string M_StudentListSet => Text(nameof(M_StudentListSet));
    public static string M_LotteryCountSet => Text(nameof(M_LotteryCountSet));
    public static string M_PoolSet => Text(nameof(M_PoolSet));
    public static string M_LotteryGroupSet => Text(nameof(M_LotteryGroupSet));
    public static string M_LotteryGenderSet => Text(nameof(M_LotteryGenderSet));
    public static string M_UnsupportedRollCall => Text(nameof(M_UnsupportedRollCall));
    public static string M_UnsupportedLottery => Text(nameof(M_UnsupportedLottery));
    public static string M_MissingProfileName => Text(nameof(M_MissingProfileName));
    public static string M_UnsupportedData => Text(nameof(M_UnsupportedData));
    public static string M_RollCallListNotFound => Text(nameof(M_RollCallListNotFound));
    public static string M_PrizePoolNotFound => Text(nameof(M_PrizePoolNotFound));
    public static string M_RollCallHistoryNotFound => Text(nameof(M_RollCallHistoryNotFound));
    public static string M_PrizeHistoryNotFound => Text(nameof(M_PrizeHistoryNotFound));
    public static string M_RollCallListLoaded => Text(nameof(M_RollCallListLoaded));
    public static string M_PrizePoolLoaded => Text(nameof(M_PrizePoolLoaded));
    public static string M_RollCallHistoryLoaded => Text(nameof(M_RollCallHistoryLoaded));
    public static string M_PrizeHistoryLoaded => Text(nameof(M_PrizeHistoryLoaded));
    public static string M_AuthorizationDenied => Text(nameof(M_AuthorizationDenied));
    public static string M_InvalidStartState => Text(nameof(M_InvalidStartState));
    public static string M_NoQuickDrawResult => Text(nameof(M_NoQuickDrawResult));
    public static string M_NoEligibleRecordsFormat => Text(nameof(M_NoEligibleRecordsFormat));
    public static string M_InvalidParameterFormat => Text(nameof(M_InvalidParameterFormat));
    public static string M_InvalidStudentList => Text(nameof(M_InvalidStudentList));
    public static string M_InvalidPool => Text(nameof(M_InvalidPool));
    public static string M_AssignmentUnavailable => Text(nameof(M_AssignmentUnavailable));
    public static string L_RollCallCount => Text(nameof(L_RollCallCount));
    public static string L_RollCallGroup => Text(nameof(L_RollCallGroup));
    public static string L_RollCallGender => Text(nameof(L_RollCallGender));
    public static string L_LotteryCount => Text(nameof(L_LotteryCount));
    public static string L_LotteryGroup => Text(nameof(L_LotteryGroup));
    public static string L_LotteryGender => Text(nameof(L_LotteryGender));
}
