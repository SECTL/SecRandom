using System;
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
    private bool _isApplyingProgrammaticChange;
    private bool _isSubscribed;

    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.Basic;
        DataContext = this;
        InitializeComponent();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }
    public CrashRecoverySettingsConfig CrashRecoverySettings => ViewModel.Config.General.CrashRecovery;
    public bool IsUiAccessSupported => OperatingSystem.IsWindows();
    public string MainWindowTopmostModeDescription => IsUiAccessSupported
        ? LR.S_Behavior_MainWindowTopmostMode_D
        : LR.S_Behavior_MainWindowTopmostMode_NonWindows_D;
    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DesktopIntegrationService DesktopIntegration { get; } = IAppHost.GetService<DesktopIntegrationService>();
    public bool IsDesktop => App.IsDesktop;

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged += CrashRecoverySettingsOnPropertyChanged;
        _isSubscribed = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged -= CrashRecoverySettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isApplyingProgrammaticChange)
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

        if (e.PropertyName == nameof(Settings.MainWindowTopmostMode)
            && Settings.MainWindowTopmostMode == TopmostMode.UiAccess
            && !DesktopIntegration.IsUiAccessAvailable())
            SettingsView.Current?.RequestRestartApp();
    }

    private void RevertDesktopIntegration(string propertyName, bool value, string title, string error)
    {
        _isApplyingProgrammaticChange = true;
        if (propertyName == nameof(Settings.Autostart))
            Settings.Autostart = value;
        else
            Settings.UrlProtocol = value;
        _isApplyingProgrammaticChange = false;

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
