using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Linkage;

[PageInfo("settings.linkage", FluentIcons.CalendarLtrRegular)]
public partial class LinkageSettingsPage : UserControl
{
    public LinkageSettingsPage()
    {
        Settings = ViewModel.Config.LinkageSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public LinkageSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }
}
