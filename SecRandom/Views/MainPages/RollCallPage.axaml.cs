using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Services.ViewEngine;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.rollCall", FluentIcons.PeopleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public sealed partial class RollCallPage : UserControl
{
    public RollCallPage()
    {
        ViewModel = IAppHost.GetService<RollCallPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public RollCallPageViewModel ViewModel { get; }

    private void RemainingListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRemainingList();
        _ = IAppHost.GetService<RemainingListViewService>().ShowAsync(
            SR.C_RemainingListTitle, ViewModel.RemainingItems, SR.M_NoRemainingStudents);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
