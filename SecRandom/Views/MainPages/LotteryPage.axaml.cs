using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Services.ViewEngine;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.Lottery.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.lottery", FluentIcons.GiftFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public sealed partial class LotteryPage : UserControl
{
    public LotteryPage()
    {
        ViewModel = IAppHost.GetService<LotteryPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public LotteryPageViewModel ViewModel { get; }

    private void RemainingListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRemainingList();
        _ = IAppHost.GetService<RemainingListViewService>().ShowAsync(
            SR.C_RemainingListTitle, ViewModel.RemainingItems, SR.M_NoRemainingPrizes);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
