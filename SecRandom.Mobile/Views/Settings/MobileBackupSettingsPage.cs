using Avalonia.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileBackupSettingsPage : UserControl
{
    internal MobileBackupSettingsPage(Action goBack)
    {
        Content = MobileUi.CreateSettingsScroll(LR.S_Backup, LR.S_Backup_D, goBack, [
            new TextBlock
            {
                Text = LR.M_BackupMobileUnavailable,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        ]);
    }
}
