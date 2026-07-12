using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.SettingsPages;

namespace SecRandom.Views.SettingsPages;

[PageInfo("settings.overview", FluentIcons.HomeRegular)]
public partial class HomeSettingsPage : UserControl
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
