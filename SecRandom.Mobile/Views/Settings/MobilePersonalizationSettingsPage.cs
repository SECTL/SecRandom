using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobilePersonalizationSettingsPage : ViewBase
{
    public MobilePersonalizationSettingsPage(MainConfigHandler configHandler)
    {
        Render(configHandler);
    }

    private void Render(MainConfigHandler configHandler)
    {
        var appearance = configHandler.Data.Appearance;
        Content = MobileUi.CreateSettingsScroll(LR.S_Personalization, LR.S_Personalization_D, CloseView, [
            MobileUi.CreateLabel(LR.S_Theme),
            MobileUi.CreateChoiceRow(LR.O_ThemeAuto, appearance.Theme == ThemeMode.Auto, () => ApplyTheme(configHandler, ThemeMode.Auto)),
            MobileUi.CreateChoiceRow(LR.O_ThemeLight, appearance.Theme == ThemeMode.Light, () => ApplyTheme(configHandler, ThemeMode.Light)),
            MobileUi.CreateChoiceRow(LR.O_ThemeDark, appearance.Theme == ThemeMode.Dark, () => ApplyTheme(configHandler, ThemeMode.Dark))
        ]);
    }

    private void ApplyTheme(MainConfigHandler configHandler, ThemeMode theme)
    {
        configHandler.Data.Appearance.Theme = theme;
        configHandler.Save();
        MobileTheme.Apply(theme);
        Render(configHandler);
    }

    private void CloseView() => _ = CloseAsync(reason: ViewCloseReason.Back);
}
