using Avalonia.Controls;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileBackupSettingsPage : ViewBase
{
    public MobileBackupSettingsPage()
    {
        Content = MobileUi.CreateSettingsScroll(LR.S_Backup, LR.S_Backup_D, CloseView, [
            new TextBlock
            {
                Text = LR.M_BackupMobileUnavailable,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        ]);
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
