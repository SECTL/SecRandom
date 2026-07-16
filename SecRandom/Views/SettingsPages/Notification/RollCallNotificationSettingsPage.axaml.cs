using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

[PageInfo("settings.notification.rollCall", FluentIcons.PersonFilled, "settings.notification")]
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

    public override string EnabledTitle => LR.S_RollCall_Enabled;
    public override string EnabledDescription => LR.S_RollCall_Enabled_D;
}
