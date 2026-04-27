using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Services.Config;

namespace SecRandom.ViewModels;

public partial class MainViewModel(MainConfigHandler configHandler)
    : ViewModelBase(configHandler)
{
    [ObservableProperty] private object? _frameContent;
    [ObservableProperty] private PageInfo? _selectedPageInfo = null;
    [ObservableProperty] private NavigationViewItemBase? _selectedNavigationViewItem = null;
    public ObservableCollection<NavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<NavigationViewItemBase> NavigationViewFooterItems { get; } = [];

    [ObservableProperty] private bool _isDrawerOpen = false;
    [ObservableProperty] private object? _drawerContent = false;
    [ObservableProperty] private bool _isRequestedRestart = false;
}