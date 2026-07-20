using Avalonia.Controls;
using SecRandom.Core.Icons;
using SecRandom.Core.Views;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 设置目录页：七个设置目的地按「偏好 / 数据 / 应用」分组，目录项用
/// <see cref="MobileSettingRow"/> 导航行，分组用 <see cref="MobileSectionHeader"/>。
/// 更新入口按 <see cref="MobileCapabilities.SupportsInAppUpdate"/> 投影（iOS 不显示）。
/// 目录是底部导航的根级目的地，因此不显示返回按钮。
/// </summary>
public sealed class MobileSettingsCatalogPage : MobileSettingsPageBase
{
    public MobileSettingsCatalogPage(IViewEngine viewEngine)
    {
        var items = new List<Control>
        {
            new MobileSectionHeader(LR.S_GroupPreferences, FluentIcons.ColorFilled),
            MobileSettingRow.Navigation(LR.S_General, LR.S_General_D, () => Show(viewEngine, MobileRoutes.General)),
            MobileSettingRow.Navigation(LR.S_Personalization, LR.S_Personalization_D, () => Show(viewEngine, MobileRoutes.Personalization)),
            MobileSettingRow.Navigation(LR.S_DrawSettings, LR.S_DrawSettings_D, () => Show(viewEngine, MobileRoutes.DrawSettings)),
            new MobileSectionHeader(LR.S_GroupData, FluentIcons.DatabaseFilled),
            MobileSettingRow.Navigation(LR.S_ListManagement, LR.S_ListManagement_D, () => Show(viewEngine, MobileRoutes.ListManagement)),
            MobileSettingRow.Navigation(LR.S_Backup, LR.S_Backup_D, () => Show(viewEngine, MobileRoutes.Backup)),
            new MobileSectionHeader(LR.S_GroupApp, FluentIcons.InfoFilled)
        };
        if (SupportsInAppUpdate)
            items.Add(MobileSettingRow.Navigation(LR.S_AppUpdates, LR.S_AppUpdates_D, () => Show(viewEngine, MobileRoutes.Update)));
        items.Add(MobileSettingRow.Navigation(LR.S_About, LR.S_About_D, () => Show(viewEngine, MobileRoutes.About)));

        Content = BuildPage(LR.P_Settings, LR.S_SettingsCategories, items, showBackButton: false);
    }

    private static void Show(IViewEngine viewEngine, string routeId) =>
        _ = viewEngine.ShowAsync(routeId);
}
