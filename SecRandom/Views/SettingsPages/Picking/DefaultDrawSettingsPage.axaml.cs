using System.ComponentModel;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;
using SecRandom.Services.Music;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.default", FluentIcons.DocumentBulletListCubeFilled, "settings.picking")]
public partial class DefaultDrawSettingsPage : UserControl
{
    private bool _isSubscribed;

    public DefaultDrawSettingsPage()
    {
        Settings = ViewModel.Config.DefaultDrawSettings;
        MusicLibrary.Refresh();
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public DefaultDrawSettingsConfig Settings { get; }
    public ObservableCollection<MusicSelection> MusicSelections => MusicLibrary.Selections;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();
    private MusicLibraryService MusicLibrary { get; } = IAppHost.GetService<MusicLibraryService>();

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        MusicLibrary.Refresh();
        SubscribeSettings();

        MusicSettingsExpander.IsExpanded = true;
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSubscribed = false;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }

    private void SubscribeSettings()
    {
        if (_isSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSubscribed = true;
    }
}
