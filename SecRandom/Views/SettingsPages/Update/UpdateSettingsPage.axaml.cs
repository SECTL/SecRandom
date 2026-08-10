using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Updates;
using SecRandom.Shared.Updates;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Update;

[PageInfo("settings.update", FluentIcons.ArrowSyncFilled, location: PageLocation.Bottom)]
public partial class UpdateSettingsPage : UserControl
{
    public UpdateSettingsPage()
    {
        Settings = ViewModel.Config.UpdateSettings;
        DataContext = this;
        InitializeComponent();
        Settings.PropertyChanged += SettingsOnPropertyChanged;
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public UpdateSettingsConfig Settings { get; }
    public UpdateCenterService UpdateCenter { get; } = IAppHost.GetService<UpdateCenterService>();

    public int SelectedChannelIndex
    {
        get => (int)Settings.UpdateChannel;
        set => Settings.UpdateChannel = Enum.IsDefined((UpdateChannel)value) ? (UpdateChannel)value : UpdateChannel.Release;
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private async void CheckForUpdates(object? sender, RoutedEventArgs e)
    {
        if (UpdateCenter.CanDownloadAndInstall)
            await UpdateCenter.DownloadAndInstallAsync();
        else
            await UpdateCenter.CheckAsync();
    }

    private async void InstallUpdate(object? sender, RoutedEventArgs e)
    {
        await UpdateCenter.ApplyDownloadedUpdateAsync();
    }

    private async void ForceCheckForUpdates(object? sender, RoutedEventArgs e)
    {
        await UpdateCenter.CheckAsync(force: true);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ConfigHandler.Save();
    }
}
