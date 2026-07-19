using Avalonia.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileGeneralSettingsPage : UserControl
{
    internal MobileGeneralSettingsPage(Action goBack)
    {
        Content = MobileUi.CreateSettingsScroll(LR.S_General, LR.S_General_D, goBack, [
            new TextBlock
            {
                Text = LR.M_GeneralMobileOnly,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        ]);
    }
}
