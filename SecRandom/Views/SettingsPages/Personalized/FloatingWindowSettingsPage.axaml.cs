using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Models;
using SecRandom.Services.Desktop;
using SecRandom.ViewModels;
using SecRandom.Views;
using LR = SecRandom.Langs.SettingsPages.FloatingWindow.Resources;

namespace SecRandom.Views.SettingsPages.Personalized;

[PageInfo("settings.personalized.floatingWindow", FluentIcons.WindowAppsFilled, "settings.personalized")]
public partial class FloatingWindowSettingsPage : UserControl
{
    private bool _isSettingsSubscribed;

    public FloatingWindowSettingsPage()
    {
        Settings = ViewModel.Config.FloatingWindowSettings;
        var migratedSize = NormalizeFloatingWindowSize() | NormalizeDockedWindowSize();
        ButtonOptions =
        [
            new(LR.S_Buttons_RollCall, () => Settings.ShowRollCallButton,
                value => Settings.ShowRollCallButton = value),
            new(LR.S_Buttons_QuickDraw, () => Settings.ShowQuickDrawButton,
                value => Settings.ShowQuickDrawButton = value),
            new(LR.S_Buttons_Lottery, () => Settings.ShowLotteryButton,
                value => Settings.ShowLotteryButton = value)
        ];
        SelectedButtonOptions = BuildSelectedOptions(ButtonOptions);
        SelectedButtonOptions.CollectionChanged += SelectedButtonOptions_OnCollectionChanged;
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        if (migratedSize)
            ConfigHandler.Save();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public FloatingWindowSettingsConfig Settings { get; }
    public AvaloniaList<MultiSelectSettingOption> ButtonOptions { get; }
    public AvaloniaList<MultiSelectSettingOption> SelectedButtonOptions { get; }
    public bool IsUiAccessSupported => OperatingSystem.IsWindows();

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private DesktopIntegrationService DesktopIntegration { get; } = IAppHost.GetService<DesktopIntegrationService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        SubscribeSettings();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private void SubscribeSettings()
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
        if (e.PropertyName == nameof(Settings.FloatingWindowTopmostMode)
            && Settings.FloatingWindowTopmostMode == TopmostMode.UiAccess
            && !DesktopIntegration.IsUiAccessAvailable())
            SettingsView.Current?.RequestRestartApp();
    }

    private static AvaloniaList<MultiSelectSettingOption> BuildSelectedOptions(
        IEnumerable<MultiSelectSettingOption> options)
    {
        return new AvaloniaList<MultiSelectSettingOption>(options.Where(option => option.IsSelected));
    }

    private bool NormalizeFloatingWindowSize()
    {
        var size = Settings.FloatingWindowSize <= 6
            ? Settings.FloatingWindowSize switch
            {
                0 => 28,
                1 => 32,
                2 => 40,
                3 => 48,
                4 => 56,
                5 => 64,
                _ => 72
            }
            : System.Math.Clamp(Settings.FloatingWindowSize, 32, 160);

        if (size == Settings.FloatingWindowSize)
            return false;

        Settings.FloatingWindowSize = size;
        return true;
    }

    private bool NormalizeDockedWindowSize()
    {
        var size = System.Math.Clamp(Settings.DockedWindowSize, 28, 96);
        if (size == Settings.DockedWindowSize)
            return false;

        Settings.DockedWindowSize = size;
        return true;
    }

    private void SelectedButtonOptions_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Settings.ShowRollCallButton = SelectedButtonOptions.Contains(ButtonOptions[0]);
        Settings.ShowQuickDrawButton = SelectedButtonOptions.Contains(ButtonOptions[1]);
        Settings.ShowLotteryButton = SelectedButtonOptions.Contains(ButtonOptions[2]);
        ConfigHandler.Save();
    }
}
