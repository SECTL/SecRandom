using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.notification.quickDraw", FluentIcons.FlashRegular, "settings.notification")]
public partial class QuickDrawNotificationSettingsPage : NotificationChannelSettingsPageBase
{
    public QuickDrawNotificationSettingsPage()
    {
        InitializeComponent();
    }

    protected override NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings)
    {
        return settings.QuickDraw;
    }

    public override string ChannelTitle => LR.S_QuickDraw;
    public override string EnabledTitle => LR.S_QuickDraw_Enabled;
    public override string EnabledDescription => LR.S_QuickDraw_Enabled_D;
    public override string AnimationTitle => LR.S_QuickDraw_Animation;
    public override string AnimationDescription => LR.S_QuickDraw_Animation_D;
    public override string AutoCloseTimeTitle => LR.S_QuickDraw_AutoCloseTime;
    public override string AutoCloseTimeDescription => LR.S_QuickDraw_AutoCloseTime_D;
    public override string WindowPositionTitle => LR.S_QuickDraw_WindowPosition;
    public override string WindowPositionDescription => LR.S_QuickDraw_WindowPosition_D;
    public override string OffsetTitle => LR.S_QuickDraw_Offset;
    public override string OffsetDescription => LR.S_QuickDraw_Offset_D;
    public override string TransparencyTitle => LR.S_QuickDraw_Transparency;
    public override string TransparencyDescription => LR.S_QuickDraw_Transparency_D;
    public override string DisplayDurationTitle => LR.S_QuickDraw_DisplayDuration;
    public override string DisplayDurationDescription => LR.S_QuickDraw_DisplayDuration_D;
    public override string UseMainWindowWhenExceedThresholdTitle => LR.S_QuickDraw_UseMainWindowWhenExceedThreshold;
    public override string UseMainWindowWhenExceedThresholdDescription => LR.S_QuickDraw_UseMainWindowWhenExceedThreshold_D;
    public override string MainWindowDisplayThresholdTitle => LR.S_QuickDraw_MainWindowDisplayThreshold;
    public override string MainWindowDisplayThresholdDescription => LR.S_QuickDraw_MainWindowDisplayThreshold_D;
}
