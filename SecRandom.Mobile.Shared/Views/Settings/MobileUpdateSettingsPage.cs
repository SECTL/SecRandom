using Avalonia.Controls;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileUpdateSettingsPage : ViewBase
{
    private readonly MobileUpdateService _updateService;

    public MobileUpdateSettingsPage(MobileUpdateService updateService)
    {
        _updateService = updateService;
        Render();
    }

    private void Render()
    {
        Content = MobileUi.CreateSettingsScroll(LR.S_AppUpdates, LR.S_AppUpdates_D, CloseView, [
            MobileUi.CreateSecondaryButton(LR.C_CheckUpdates, async () =>
            {
                await _updateService.CheckAsync();
                Render();
            }),
            new TextBlock
            {
                Text = _updateService.Status,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            },
            MobileUi.CreatePrimaryButton(LR.C_InstallUpdate, _updateService.IsUpdateAvailable && !_updateService.IsBusy, async () =>
            {
                await _updateService.DownloadAndInstallAsync();
                Render();
            })
        ]);
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
