using Avalonia.Controls;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileAboutSettingsPage : ViewBase
{
    public MobileAboutSettingsPage()
    {
        var version = typeof(MobileAboutSettingsPage).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        Content = MobileUi.CreateSettingsScroll(LR.S_About, LR.S_About_D, CloseView, [
            MobileUi.CreateMetric(LR.S_Version, version, MobileTheme.SurfaceMuted),
            new TextBlock
            {
                Text = LR.M_AboutLicense,
                Foreground = MobileTheme.MutedText,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            }
        ]);
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
