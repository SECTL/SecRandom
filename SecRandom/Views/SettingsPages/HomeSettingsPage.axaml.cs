using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Views;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.SettingsPages;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.overview", FluentIcons.HomeFilled)]
public partial class HomeSettingsPage : ViewBase
{
    public HomeSettingsPage()
    {
        ViewModel = IAppHost.GetService<HomeSettingsPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public HomeSettingsPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e) => ViewModel.Refresh();
}
