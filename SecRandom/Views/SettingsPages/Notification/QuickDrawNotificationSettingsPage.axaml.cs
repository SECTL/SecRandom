using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.quickDraw", FluentIcons.FlashFilled, "settings.notification")]
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

    public override string EnabledTitle => LR.S_QuickDraw_Enabled;
    public override string EnabledDescription => LR.S_QuickDraw_Enabled_D;
}
