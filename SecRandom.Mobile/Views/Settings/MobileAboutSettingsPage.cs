using Avalonia.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileAboutSettingsPage : UserControl
{
    internal MobileAboutSettingsPage(Action goBack)
    {
        var version = typeof(MobileAboutSettingsPage).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        Content = MobileUi.CreateSettingsScroll(LR.S_About, LR.S_About_D, goBack, [
            MobileUi.CreateMetric(LR.S_Version, version, MobileTheme.SurfaceMuted),
            new TextBlock
            {
                Text = LR.M_AboutLicense,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        ]);
    }
}
