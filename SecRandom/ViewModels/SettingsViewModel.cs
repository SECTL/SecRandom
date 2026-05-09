using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Models;
using SecRandom.Services;

namespace SecRandom.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private object? _drawerContent = false;

    [ObservableProperty] private object? _frameContent;

    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isRequestedRestart;
    [ObservableProperty] private NavigationViewItemBase? _selectedNavigationViewItem;
    [ObservableProperty] private PageInfo? _selectedPageInfo;
    [ObservableProperty] private SettingsMetadata? _selectedSettings;

    public SettingsViewModel(SettingsSearchService settingsSearchService)
    {
        SettingsSearchService = settingsSearchService;
        SettingsMetadata = SettingsSearchService.SettingsMetadata;
    }

    public bool IsWindows => OperatingSystem.IsWindows();
    public ObservableCollection<NavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    public ObservableCollection<string> NavigationHistory { get; } = [];

    public SettingsSearchService SettingsSearchService { get; }
    public List<SettingsMetadata> SettingsMetadata { get; }
}