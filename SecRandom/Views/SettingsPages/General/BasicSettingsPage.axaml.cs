using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FeatureConfiguration;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels;
using LR = SecRandom.Langs.SettingsPages.General.Basic.Resources;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.WrenchSettingsFilled, "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    private bool _isApplyingProgrammaticChange;
    private bool _isSubscribed;
    private bool _updatingFeatureProfiles;

    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.Basic;
        DataContext = this;
        InitializeComponent();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }
    public CrashRecoverySettingsConfig CrashRecoverySettings => ViewModel.Config.General.CrashRecovery;
    public PerformanceSettingsConfig PerformanceSettings => ViewModel.Config.General.PerformanceSettings;
    public SimpleModeSettingsConfig SimpleMode => ViewModel.Config.General.SimpleMode;
    public string FeatureConfigureLabel => FeatureConfigurationText.Get("Configure");
    public string FeatureTitle => FeatureConfigurationText.Get("Title");
    public string FeatureDescription => FeatureConfigurationText.Get("EntryDescription");
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
        PerformanceSettings.PropertyChanged += PerformanceSettingsOnPropertyChanged;
        RefreshFeatureProfiles();
        _isSubscribed = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        CrashRecoverySettings.PropertyChanged -= CrashRecoverySettingsOnPropertyChanged;
        PerformanceSettings.PropertyChanged -= PerformanceSettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void PerformanceSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
        App.Current.RefreshPerformanceSettings();

        // 动画与图片质量可以即时生效，但窗口合成级别（Mica / 透明）只能在窗口构造期决定，
        // 已经打开的窗口不会变化，因此按项目约定请求重启，避免出现"看起来没生效"。
        SettingsView.Current?.RequestRestartApp();
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

    private void RefreshFeatureProfiles()
    {
        _updatingFeatureProfiles = true;
        try
        {
            var selectedId = StartupFeatureCatalog.ResolveSelectedProfileId(SimpleMode);
            var items = new List<ComboBoxItem>
            {
                new() { Content = FeatureConfigurationText.Get("Full"), Tag = StartupFeatureCatalog.FullProfile },
                new() { Content = FeatureConfigurationText.Get("Simple"), Tag = StartupFeatureCatalog.SimpleProfile }
            };
            foreach (var profile in SimpleMode.Profiles ?? [])
                items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile.Id });

            FeatureProfileSelector.SelectedItem = null;
            FeatureProfileSelector.ItemsSource = items;
            FeatureProfileSelector.SelectedItem = items.FirstOrDefault(item => Equals(item.Tag, selectedId));
        }
        finally
        {
            _updatingFeatureProfiles = false;
        }
    }

    private async void FeatureProfileSelector_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingFeatureProfiles || FeatureProfileSelector.SelectedItem is not ComboBoxItem { Tag: string id })
            return;
        var currentId = StartupFeatureCatalog.ResolveSelectedProfileId(SimpleMode);
        if (id == currentId) return;
        _updatingFeatureProfiles = true;
        var result = await new FAContentDialog
        {
            Title = FeatureConfigurationText.Get("Title"),
            Content = FeatureConfigurationText.Get("ApplyConfirmation"),
            PrimaryButtonText = FeatureConfigurationText.Get("Confirm"),
            CloseButtonText = FeatureConfigurationText.Get("Cancel")
        }.ShowAsync(TopLevel.GetTopLevel(this));
        _updatingFeatureProfiles = false;
        if (result != FAContentDialogResult.Primary)
        {
            RefreshFeatureProfiles();
            return;
        }
        _isApplyingProgrammaticChange = true;
        SimpleMode.SelectedProfileId = id;
        _isApplyingProgrammaticChange = false;
        ConfigHandler.Save();
        SettingsView.Current?.RequestRestartApp();
    }

    private void ConfigureFeatures_OnClick(object? sender, RoutedEventArgs e)
    {
        App.ShowFeatureConfigurationWindow(TopLevel.GetTopLevel(this) as Window);
    }

}
