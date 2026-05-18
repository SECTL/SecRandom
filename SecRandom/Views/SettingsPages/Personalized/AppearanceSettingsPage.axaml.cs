using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Personalized;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Personalized;

[PageInfo("settings.personalized.appearance", FluentIcons.LayerDiagonalSparkleRegular, "settings.personalized")]
public partial class AppearanceSettingsPage : UserControl
{
    public AppearanceSettingsPage()
    {
        Settings = ViewModel.Config.Appearance;
        DataContext = this;
        InitializeComponent();

        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public AppearanceSettingsConfig Settings { get; }

    public List<FontFamily> FontFamilies { get; } = [..FontManager.Current.SystemFonts, App.DefaultFontFamily];

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        App.Current.RefreshPersonalizedSettings();
    }
}