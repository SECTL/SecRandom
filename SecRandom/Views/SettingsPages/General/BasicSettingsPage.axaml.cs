using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.General;

[PageInfo("settings.general.basic", FluentIcons.WrenchSettingsRegular, "settings.general")]
public partial class BasicSettingsPage : UserControl
{
    public BasicSettingsPage()
    {
        Settings = ViewModel.Config.BasicSettings;
        DataContext = this;
        InitializeComponent();

        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public BasicSettingsConfig Settings { get; }

    public List<FontFamily> FontFamilies { get; } = [..FontManager.Current.SystemFonts, App.DefaultFontFamily];

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.Language)) SettingsView.Current?.RequestRestartApp();

        App.Current.RefreshPersonalizedSettings();
    }

    private void HideVersionNoticeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Settings.ShowVersionNotice = false;
    }

    private void OpenBackupManagerButton_OnClick(object? sender, RoutedEventArgs e)
    {
        SettingsView.Current?.SelectNavigationItemById("settings.basic.backup");
    }
}