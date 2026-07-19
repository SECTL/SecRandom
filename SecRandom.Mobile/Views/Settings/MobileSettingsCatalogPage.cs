using Avalonia.Controls;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

public sealed class MobileSettingsCatalogPage : ViewBase
{
    public MobileSettingsCatalogPage(IViewEngine viewEngine)
    {
        Content = MobileUi.CreateScroll([
            MobileUi.CreateLabel(LR.S_SettingsCategories),
            MobileUi.CreateTitle(LR.P_Settings),
            MobileUi.CreateNavigationRow(LR.S_General, LR.S_General_D, () => Show(viewEngine, MobileRoutes.General)),
            MobileUi.CreateNavigationRow(LR.S_Personalization, LR.S_Personalization_D, () => Show(viewEngine, MobileRoutes.Personalization)),
            MobileUi.CreateNavigationRow(LR.S_ListManagement, LR.S_ListManagement_D, () => Show(viewEngine, MobileRoutes.ListManagement)),
            MobileUi.CreateNavigationRow(LR.S_DrawSettings, LR.S_DrawSettings_D, () => Show(viewEngine, MobileRoutes.DrawSettings)),
            MobileUi.CreateNavigationRow(LR.S_Backup, LR.S_Backup_D, () => Show(viewEngine, MobileRoutes.Backup)),
            MobileUi.CreateNavigationRow(LR.S_AppUpdates, LR.S_AppUpdates_D, () => Show(viewEngine, MobileRoutes.Update)),
            MobileUi.CreateNavigationRow(LR.S_About, LR.S_About_D, () => Show(viewEngine, MobileRoutes.About))
        ]);
    }

    private static void Show(IViewEngine viewEngine, string routeId) =>
        _ = viewEngine.ShowAsync(routeId);
}
