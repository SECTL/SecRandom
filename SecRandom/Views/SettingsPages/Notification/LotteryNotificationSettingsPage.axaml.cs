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

    public override string EnabledTitle => LR.S_Lottery_Enabled;
    public override string EnabledDescription => LR.S_Lottery_Enabled_D;
}
