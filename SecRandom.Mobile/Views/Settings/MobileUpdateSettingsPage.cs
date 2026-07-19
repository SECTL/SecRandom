using Avalonia.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileUpdateSettingsPage : UserControl
{
    internal MobileUpdateSettingsPage(MobileUpdateService updateService, Action goBack, Action refresh)
    {
        Content = MobileUi.CreateSettingsScroll(LR.S_AppUpdates, LR.S_AppUpdates_D, goBack, [
            MobileUi.CreateSecondaryButton(LR.C_CheckUpdates, async () =>
            {
                await updateService.CheckAsync();
                refresh();
            }),
            new TextBlock
            {
                Text = updateService.Status,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            MobileUi.CreatePrimaryButton(LR.C_InstallUpdate, updateService.IsUpdateAvailable && !updateService.IsBusy, async () =>
            {
                await updateService.DownloadAndInstallAsync();
                refresh();
            })
        ]);
    }
}
