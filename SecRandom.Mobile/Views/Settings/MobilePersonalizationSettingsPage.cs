using Avalonia.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobilePersonalizationSettingsPage : UserControl
{
    internal MobilePersonalizationSettingsPage(
        MainConfigHandler configHandler,
        Action goBack,
        Action<ThemeMode> applyTheme)
    {
        var appearance = configHandler.Data.Appearance;
        Content = MobileUi.CreateSettingsScroll(LR.S_Personalization, LR.S_Personalization_D, goBack, [
            MobileUi.CreateLabel(LR.S_Theme),
            MobileUi.CreateChoiceRow(LR.O_ThemeAuto, appearance.Theme == ThemeMode.Auto, () => applyTheme(ThemeMode.Auto)),
            MobileUi.CreateChoiceRow(LR.O_ThemeLight, appearance.Theme == ThemeMode.Light, () => applyTheme(ThemeMode.Light)),
            MobileUi.CreateChoiceRow(LR.O_ThemeDark, appearance.Theme == ThemeMode.Dark, () => applyTheme(ThemeMode.Dark))
        ]);
    }
}
