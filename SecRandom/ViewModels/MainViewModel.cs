using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Attributes;
using SecRandom.Core.Services.Config;

namespace SecRandom.ViewModels;

public partial class MainViewModel(MainConfigHandler configHandler)
    : ViewModelBase(configHandler)
{
    [ObservableProperty] private object? _drawerContent = false;
    [ObservableProperty] private object? _frameContent;

    [ObservableProperty] private bool _isDrawerOpen;
    [ObservableProperty] private bool _isRequestedRestart;
    [ObservableProperty] private FANavigationViewItemBase? _selectedNavigationViewItem;
    [ObservableProperty] private PageInfo? _selectedPageInfo;
    public ObservableCollection<FANavigationViewItemBase> FlattenNavigationItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewItems { get; } = [];
    public ObservableCollection<FANavigationViewItemBase> NavigationViewFooterItems { get; } = [];
}