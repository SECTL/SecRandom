using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.SettingsPages;

namespace SecRandom.Views.SettingsPages.Picking;

[PageInfo("settings.picking.randomNumber", FluentIcons.FlashFilled, "settings.picking")]
public partial class RandomNumberSettingsPage : UserControl
{
    public RandomNumberSettingsPage()
    {
        ViewModel = IAppHost.GetService<RandomNumberSettingsPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public RandomNumberSettingsPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e) => ViewModel.Attach();

    private void OnUnloaded(object? sender, RoutedEventArgs e) => ViewModel.Detach();
}