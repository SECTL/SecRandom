using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.notification.legacy", FluentIcons.CommentNoteRegular)]
public partial class NotificationSettingsPage : UserControl
{
    public NotificationSettingsPage()
    {
        Settings = ViewModel.Config.NotificationSettings;
        DataContext = this;
        InitializeComponent();
        Settings.RollCall.PropertyChanged += SettingsOnPropertyChanged;
        Settings.QuickDraw.PropertyChanged += SettingsOnPropertyChanged;
        Settings.Lottery.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public NotificationSettingsConfig Settings { get; }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.RollCall.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.QuickDraw.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.Lottery.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }
}
