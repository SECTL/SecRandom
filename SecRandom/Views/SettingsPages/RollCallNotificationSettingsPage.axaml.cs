using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.notification.rollCall", FluentIcons.PersonRegular, "settings.notification")]
public partial class RollCallNotificationSettingsPage : NotificationChannelSettingsPageBase
{
    public RollCallNotificationSettingsPage()
    {
        InitializeComponent();
    }

    protected override NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings)
    {
        return settings.RollCall;
    }

    public override string ChannelTitle => LR.S_RollCall;
    public override string EnabledTitle => LR.S_RollCall_Enabled;
    public override string EnabledDescription => LR.S_RollCall_Enabled_D;
    public override string AnimationTitle => LR.S_RollCall_Animation;
    public override string AnimationDescription => LR.S_RollCall_Animation_D;
    public override string AutoCloseTimeTitle => LR.S_RollCall_AutoCloseTime;
    public override string AutoCloseTimeDescription => LR.S_RollCall_AutoCloseTime_D;
    public override string WindowPositionTitle => LR.S_RollCall_WindowPosition;
    public override string WindowPositionDescription => LR.S_RollCall_WindowPosition_D;
    public override string OffsetTitle => LR.S_RollCall_Offset;
    public override string OffsetDescription => LR.S_RollCall_Offset_D;
    public override string TransparencyTitle => LR.S_RollCall_Transparency;
    public override string TransparencyDescription => LR.S_RollCall_Transparency_D;
    public override string DisplayDurationTitle => LR.S_RollCall_DisplayDuration;
    public override string DisplayDurationDescription => LR.S_RollCall_DisplayDuration_D;
    public override string UseMainWindowWhenExceedThresholdTitle => LR.S_RollCall_UseMainWindowWhenExceedThreshold;
    public override string UseMainWindowWhenExceedThresholdDescription => LR.S_RollCall_UseMainWindowWhenExceedThreshold_D;
    public override string MainWindowDisplayThresholdTitle => LR.S_RollCall_MainWindowDisplayThreshold;
    public override string MainWindowDisplayThresholdDescription => LR.S_RollCall_MainWindowDisplayThreshold_D;
}
