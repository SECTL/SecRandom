using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views.Settings;

namespace SecRandom.Mobile.Views;

/// <summary>
/// Owns mobile settings navigation inside one MVE session.
/// </summary>
public sealed class MobileSettingsView : ViewBase
{
    private readonly IProfileService _profileService;
    private readonly IDrawTemporaryRecordService _temporaryRecordService;
    private readonly MainConfigHandler _configHandler;
    private readonly MobileUpdateService _updateService;

    public MobileSettingsView(
        IProfileService profileService,
        IDrawTemporaryRecordService temporaryRecordService,
        MainConfigHandler configHandler,
        MobileUpdateService updateService)
    {
        _profileService = profileService;
        _temporaryRecordService = temporaryRecordService;
        _configHandler = configHandler;
        _updateService = updateService;
        ShowCatalog();
    }

    private void ShowCatalog() => Content = new MobileSettingsCatalogPage(ShowSection);

    private void ShowSection(MobileSettingsSection section)
    {
        Content = section switch
        {
            MobileSettingsSection.General => new MobileGeneralSettingsPage(_configHandler, ShowCatalog),
            MobileSettingsSection.Personalization => new MobilePersonalizationSettingsPage(
                _configHandler, ShowCatalog, ApplyTheme),
            MobileSettingsSection.ListManagement => new MobileListManagementSettingsPage(
                _profileService, ShowCatalog, ShowCatalog),
            MobileSettingsSection.Draw => new MobileDrawSettingsPage(
                _configHandler, _temporaryRecordService, _profileService, ShowCatalog, ShowCatalog),
            MobileSettingsSection.Backup => new MobileBackupSettingsPage(ShowCatalog),
            MobileSettingsSection.Update => new MobileUpdateSettingsPage(_updateService, ShowCatalog, ShowCatalog),
            MobileSettingsSection.About => new MobileAboutSettingsPage(ShowCatalog),
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
    }

    private void ApplyTheme(SecRandom.Core.Enums.Configs.ThemeMode theme)
    {
        _configHandler.Data.Appearance.Theme = theme;
        _configHandler.Save();
        MobileTheme.Apply(theme);
        ShowCatalog();
    }
}
