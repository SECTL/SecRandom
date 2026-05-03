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
    public bool IsWindows => OperatingSystem.IsWindows();
    
    [ObservableProperty] private object? _frameContent;
    [ObservableProperty] private PageInfo? _selectedPageInfo = null;
    [ObservableProperty] private NavigationViewItemBase? _selectedNavigationViewItem = null;
    public ObservableCollection<NavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    public ObservableCollection<string> NavigationHistory { get; } = [];
    [ObservableProperty] private bool _canGoBack = false;

    [ObservableProperty] private bool _isDrawerOpen = false;
    [ObservableProperty] private object? _drawerContent = false;
    [ObservableProperty] private bool _isRequestedRestart = false;

    public SettingsSearchService SettingsSearchService { get; }
    public List<SettingsMetadata> SettingsMetadata { get; }
    [ObservableProperty] private SettingsMetadata? _selectedSettings = null;

    public SettingsViewModel(SettingsSearchService settingsSearchService)
    {
        SettingsSearchService = settingsSearchService;
        SettingsMetadata = SettingsSearchService.SettingsMetadata;
    }
}