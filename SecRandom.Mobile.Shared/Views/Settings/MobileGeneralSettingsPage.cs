using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileGeneralSettingsPage : ViewBase
{
    public MobileGeneralSettingsPage(MainConfigHandler configHandler)
    {
        Render(configHandler);
    }

    private void Render(MainConfigHandler configHandler)
    {
        var privacy = configHandler.Data.General.PrivacySettings;
        void Save(Action mutate)
        {
            mutate();
            configHandler.Save();
            Render(configHandler);
        }

        Content = MobileUi.CreateSettingsScroll(LR.S_General, LR.S_General_D, CloseView, [
            new TextBlock
            {
                Text = LR.M_GeneralMobileOnly,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            MobileUi.CreateLabel("在线状态上报"),
            MobileUi.CreateChoiceRow("关闭", privacy.OnlineStatusMode == OnlineStatusMode.Off,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Off)),
            MobileUi.CreateChoiceRow("匿名", privacy.OnlineStatusMode == OnlineStatusMode.Anonymous,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Anonymous)),
            MobileUi.CreateChoiceRow("完整", privacy.OnlineStatusMode == OnlineStatusMode.Full,
                () => Save(() => privacy.OnlineStatusMode = OnlineStatusMode.Full))
        ]);
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
