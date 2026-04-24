using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;

namespace SecRandom.ViewModels;

public partial class SettingsViewModel : ObservableRecipient
{
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
}