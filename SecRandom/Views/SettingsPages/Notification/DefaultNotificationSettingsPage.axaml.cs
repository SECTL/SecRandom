using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.default", FluentIcons.CommentNoteFilled, "settings.notification")]
public partial class DefaultNotificationSettingsPage : NotificationChannelSettingsPageBase
{
    public DefaultNotificationSettingsPage()
    {
        InitializeComponent();
    }

    protected override NotificationChannelSettings SelectChannelSettings(NotificationSettingsConfig settings)
    {
        return settings.Default;
    }

    public override string EnabledTitle => Text(nameof(EnabledTitle), "S_Default_Enabled");
    public override string EnabledDescription => Text(nameof(EnabledDescription), "S_Default_Enabled_D");
    public override string AnimationTitle => Text(nameof(AnimationTitle), "S_Default_Animation");
    public override string AnimationDescription => Text(nameof(AnimationDescription), "S_Default_Animation_D");
    public override string DisplayDurationTitle => Text(nameof(DisplayDurationTitle), "S_Default_DisplayDuration");
    public override string DisplayDurationDescription => Text(nameof(DisplayDurationDescription), "S_Default_DisplayDuration_D");
    public override string UseMainWindowWhenExceedThresholdTitle => Text(nameof(UseMainWindowWhenExceedThresholdTitle), "S_Default_UseMainWindowWhenExceedThreshold");
    public override string UseMainWindowWhenExceedThresholdDescription => Text(nameof(UseMainWindowWhenExceedThresholdDescription), "S_Default_UseMainWindowWhenExceedThreshold_D");
    public override string MainWindowDisplayThresholdTitle => Text(nameof(MainWindowDisplayThresholdTitle), "S_Default_MainWindowDisplayThreshold");
    public override string MainWindowDisplayThresholdDescription => Text(nameof(MainWindowDisplayThresholdDescription), "S_Default_MainWindowDisplayThreshold_D");

    private void TestNotification_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => HandleTestNotificationAsync(sender, e);

    private static string Text(string fallback, string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
    }
}
