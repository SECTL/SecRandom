using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Helpers;
using SecRandom.ViewModels;

namespace SecRandom.Views.MainPages;

[PageInfo("main.lottery", FluentIcons.LotteryFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class LotteryPage : UserControl
{
    private bool _isUnloaded;

    public LotteryPage()
    {
        ViewModel = IAppHost.GetService<LotteryPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        Unloaded += OnUnloaded;
    }

    public LotteryPageViewModel ViewModel { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
    }

    private async void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        try
        {
            if (e.PropertyName == nameof(LotteryPageViewModel.PreviewAnimationRevision))
            {
                await DrawResultAnimationHelper.PreviewSwapAsync(
                    ResultPresenter,
                    ViewModel.PreviewAnimationDuration);
            }
            else if (e.PropertyName == nameof(LotteryPageViewModel.ResultAnimationRevision))
            {
                await DrawResultAnimationHelper.RevealAsync(
                    ResultPresenter,
                    ViewModel.ResultFlowAnimationEnabled,
                    ViewModel.ResultFlowAnimationDuration);
            }
        }
        catch
        {
        }
    }
}
