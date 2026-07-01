using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.default", FluentIcons.CommentNoteRegular, "settings.notification")]
public partial class DefaultNotificationSettingsPage : NotificationChannelSettingsPageBase
{
    public DefaultNotificationSettingsPage()
    {
        InitializeComponent();
    }

    protected override NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings)
    {
        return settings.QuickDraw;
    }

    public override string ChannelTitle => Text(nameof(ChannelTitle), "S_Default");
    public override string EnabledTitle => Text(nameof(EnabledTitle), "S_Default_Enabled");
    public override string EnabledDescription => Text(nameof(EnabledDescription), "S_Default_Enabled_D");
    public override string AnimationTitle => Text(nameof(AnimationTitle), "S_Default_Animation");
    public override string AnimationDescription => Text(nameof(AnimationDescription), "S_Default_Animation_D");
    public override string AutoCloseTimeTitle => Text(nameof(AutoCloseTimeTitle), "S_Default_AutoCloseTime");
    public override string AutoCloseTimeDescription => Text(nameof(AutoCloseTimeDescription), "S_Default_AutoCloseTime_D");
    public override string WindowPositionTitle => Text(nameof(WindowPositionTitle), "S_Default_WindowPosition");
    public override string WindowPositionDescription => Text(nameof(WindowPositionDescription), "S_Default_WindowPosition_D");
    public override string OffsetTitle => Text(nameof(OffsetTitle), "S_Default_Offset");
    public override string OffsetDescription => Text(nameof(OffsetDescription), "S_Default_Offset_D");
    public override string TransparencyTitle => Text(nameof(TransparencyTitle), "S_Default_Transparency");
    public override string TransparencyDescription => Text(nameof(TransparencyDescription), "S_Default_Transparency_D");
    public override string DisplayDurationTitle => Text(nameof(DisplayDurationTitle), "S_Default_DisplayDuration");
    public override string DisplayDurationDescription => Text(nameof(DisplayDurationDescription), "S_Default_DisplayDuration_D");
    public override string UseMainWindowWhenExceedThresholdTitle => Text(nameof(UseMainWindowWhenExceedThresholdTitle), "S_Default_UseMainWindowWhenExceedThreshold");
    public override string UseMainWindowWhenExceedThresholdDescription => Text(nameof(UseMainWindowWhenExceedThresholdDescription), "S_Default_UseMainWindowWhenExceedThreshold_D");
    public override string MainWindowDisplayThresholdTitle => Text(nameof(MainWindowDisplayThresholdTitle), "S_Default_MainWindowDisplayThreshold");
    public override string MainWindowDisplayThresholdDescription => Text(nameof(MainWindowDisplayThresholdDescription), "S_Default_MainWindowDisplayThreshold_D");

    private static string Text(string fallback, string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
    }
}
