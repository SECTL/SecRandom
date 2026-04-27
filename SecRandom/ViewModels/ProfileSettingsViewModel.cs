using CommunityToolkit.Mvvm.ComponentModel;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Services.Config;

namespace SecRandom.ViewModels;

public partial class ProfileSettingsViewModel(MainConfigHandler configHandler) : ViewModelBase(configHandler)
{
    [ObservableProperty] private int _selectedPageIndex = 0;
    [ObservableProperty] private NavigationViewItemBase? _selectedPageItem;
    
    [ObservableProperty] private bool _isDrawerOpen = false;
    [ObservableProperty] private object? _drawerContent = false;
}