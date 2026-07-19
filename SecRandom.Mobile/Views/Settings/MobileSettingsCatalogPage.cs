using Avalonia.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

internal sealed class MobileSettingsCatalogPage : UserControl
{
    internal MobileSettingsCatalogPage(Action<MobileSettingsSection> openSection)
    {
        Content = MobileUi.CreateScroll([
            MobileUi.CreateLabel(LR.S_SettingsCategories),
            MobileUi.CreateTitle(LR.P_Settings),
            MobileUi.CreateNavigationRow(LR.S_General, LR.S_General_D, () => openSection(MobileSettingsSection.General)),
            MobileUi.CreateNavigationRow(LR.S_Personalization, LR.S_Personalization_D, () => openSection(MobileSettingsSection.Personalization)),
            MobileUi.CreateNavigationRow(LR.S_ListManagement, LR.S_ListManagement_D, () => openSection(MobileSettingsSection.ListManagement)),
            MobileUi.CreateNavigationRow(LR.S_DrawSettings, LR.S_DrawSettings_D, () => openSection(MobileSettingsSection.Draw)),
            MobileUi.CreateNavigationRow(LR.S_FairDraw, LR.S_FairDraw_D, () => openSection(MobileSettingsSection.FairDraw)),
            MobileUi.CreateNavigationRow(LR.S_Backup, LR.S_Backup_D, () => openSection(MobileSettingsSection.Backup)),
            MobileUi.CreateNavigationRow(LR.S_AppUpdates, LR.S_AppUpdates_D, () => openSection(MobileSettingsSection.Update)),
            MobileUi.CreateNavigationRow(LR.S_About, LR.S_About_D, () => openSection(MobileSettingsSection.About))
        ]);
    }
}
