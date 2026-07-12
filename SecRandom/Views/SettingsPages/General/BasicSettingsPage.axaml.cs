using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.General.Basic.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.WrenchSettingsFilled, "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    private bool _isRevertingDesktopIntegration;

    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.Basic;
        DataContext = this;
        InitializeComponent();

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged += CrashRecoverySettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }
    public CrashRecoverySettingsConfig CrashRecoverySettings => ViewModel.Config.General.CrashRecovery;
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DesktopIntegrationService DesktopIntegration { get; } = IAppHost.GetService<DesktopIntegrationService>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged -= CrashRecoverySettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRevertingDesktopIntegration)
            return;

        if (e.PropertyName == nameof(Settings.Language))
        {
            // 先设置语言，省的 Needs Restarting 显示中文等情况发生
            var culture = Settings.Language switch
            {
                LanguageMode.ChineseSimplified => @"zh-Hans",
                LanguageMode.English => @"en-US",
                LanguageMode.Japanese => @"ja-JP",
                _ => @"zh-Hans"
            };
            App.InitializeLanguages(new CultureInfo(culture));
            SettingsView.Current?.RequestRestartApp();
        }

        if (e.PropertyName == nameof(Settings.Autostart)
            && !DesktopIntegration.TrySetAutostart(Settings.Autostart, out var autostartError))
        {
            RevertDesktopIntegration(
                nameof(Settings.Autostart),
                !Settings.Autostart,
                LR.S_Behavior_Autostart,
                autostartError);
            return;
        }

        if (e.PropertyName == nameof(Settings.UrlProtocol)
            && !DesktopIntegration.TrySetUrlProtocol(Settings.UrlProtocol, out var protocolError))
        {
            RevertDesktopIntegration(
                nameof(Settings.UrlProtocol),
                !Settings.UrlProtocol,
                LR.S_Behavior_UrlProtocol,
                protocolError);
            return;
        }

        ConfigHandler.Save();
    }

    private void RevertDesktopIntegration(string propertyName, bool value, string title, string error)
    {
        _isRevertingDesktopIntegration = true;
        if (propertyName == nameof(Settings.Autostart))
            Settings.Autostart = value;
        else
            Settings.UrlProtocol = value;
        _isRevertingDesktopIntegration = false;

        ConfigHandler.Save();
        this.ShowErrorToast(string.Format(CultureInfo.CurrentCulture, LR.M_DesktopIntegrationFailed, title, error));
    }

    private void CrashRecoverySettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void HideVersionNoticeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Settings.ShowVersionNotice = false;
    }
}
