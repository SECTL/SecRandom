using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Models;
using SecRandom.Services.Settings;

namespace SecRandom.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
    [ObservableProperty] private bool _canGoBack;
    [ObservableProperty] private object? _drawerContent = false;

    [ObservableProperty] private object? _frameContent;

    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isRequestedRestart;
    [ObservableProperty] private FANavigationViewItemBase? _selectedNavigationViewItem;
    [ObservableProperty] private PageInfo? _selectedPageInfo;
    [ObservableProperty] private SettingsMetadata? _selectedSettings;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSearchText))]
    private string _searchText = string.Empty;

    public SettingsViewModel(SettingsSearchService? settingsSearchService = null)
    {
        SettingsSearchService = settingsSearchService;
        SettingsMetadata = settingsSearchService?.SettingsMetadata ?? [];
    }

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool HasSearchText => !string.IsNullOrEmpty(SearchText);
    public ObservableCollection<FANavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    public ObservableCollection<string> NavigationHistory { get; } = [];

    public SettingsSearchService? SettingsSearchService { get; }
    public List<SettingsMetadata> SettingsMetadata { get; }
}
