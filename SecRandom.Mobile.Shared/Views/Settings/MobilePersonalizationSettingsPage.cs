using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Services.Config;
using SecRandom.Mobile.Controls;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile.Views.Settings;

/// <summary>
/// 个性化设置页：主题三档。主题选择立即保存并应用到 RequestedThemeVariant，
/// 页面内容颜色全部走 DynamicResource，由资源系统随主题刷新。
/// </summary>
public sealed class MobilePersonalizationSettingsPage : MobileSettingsPageBase
{
    private readonly MainConfigHandler _configHandler;

    public MobilePersonalizationSettingsPage(MainConfigHandler configHandler)
    {
        _configHandler = configHandler;
        Render();
    }

    private void Render()
    {
        var appearance = _configHandler.Data.Appearance;
        Content = BuildPage(LR.S_Personalization, LR.S_Personalization_D, [
            new MobileSectionHeader(LR.S_Theme, FluentIcons.ColorFilled),
            MobileSettingRow.Choice(LR.O_ThemeAuto, appearance.Theme == ThemeMode.Auto, () => ApplyTheme(ThemeMode.Auto)),
            MobileSettingRow.Choice(LR.O_ThemeLight, appearance.Theme == ThemeMode.Light, () => ApplyTheme(ThemeMode.Light)),
            MobileSettingRow.Choice(LR.O_ThemeDark, appearance.Theme == ThemeMode.Dark, () => ApplyTheme(ThemeMode.Dark))
        ]);
    }

    private void ApplyTheme(ThemeMode theme)
    {
        _configHandler.Data.Appearance.Theme = theme;
        _configHandler.Save();
        MobileTheme.Apply(theme);
        Render();
    }
}
