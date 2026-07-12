using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.lottery", FluentIcons.LotteryFilled, "settings.notification")]
public partial class LotteryNotificationSettingsPage : NotificationChannelSettingsPageBase
{
    public LotteryNotificationSettingsPage()
    {
        InitializeComponent();
    }

    protected override NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings)
    {
        return settings.Lottery;
    }

    public override string ChannelTitle => LR.S_Lottery;
    public override string EnabledTitle => LR.S_Lottery_Enabled;
    public override string EnabledDescription => LR.S_Lottery_Enabled_D;
    public override string AnimationTitle => LR.S_Lottery_Animation;
    public override string AnimationDescription => LR.S_Lottery_Animation_D;
    public override string AutoCloseTimeTitle => LR.S_Lottery_AutoCloseTime;
    public override string AutoCloseTimeDescription => LR.S_Lottery_AutoCloseTime_D;
    public override string WindowPositionTitle => LR.S_Lottery_WindowPosition;
    public override string WindowPositionDescription => LR.S_Lottery_WindowPosition_D;
    public override string OffsetTitle => LR.S_Lottery_Offset;
    public override string OffsetDescription => LR.S_Lottery_Offset_D;
    public override string TransparencyTitle => LR.S_Lottery_Transparency;
    public override string TransparencyDescription => LR.S_Lottery_Transparency_D;
    public override string DisplayDurationTitle => LR.S_Lottery_DisplayDuration;
    public override string DisplayDurationDescription => LR.S_Lottery_DisplayDuration_D;
    public override string UseMainWindowWhenExceedThresholdTitle => LR.S_Lottery_UseMainWindowWhenExceedThreshold;
    public override string UseMainWindowWhenExceedThresholdDescription => LR.S_Lottery_UseMainWindowWhenExceedThreshold_D;
    public override string MainWindowDisplayThresholdTitle => LR.S_Lottery_MainWindowDisplayThreshold;
    public override string MainWindowDisplayThresholdDescription => LR.S_Lottery_MainWindowDisplayThreshold_D;
}
