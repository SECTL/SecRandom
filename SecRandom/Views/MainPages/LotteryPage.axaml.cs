using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.Lottery.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.lottery", FluentIcons.GiftFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class LotteryPage : UserControl
{
    private bool _isUnloaded;
    private bool _isViewModelSubscribed;
    private readonly ItemsControl? _resultPresenter;

    public LotteryPage()
    {
        ViewModel = IAppHost.GetService<LotteryPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter");
        AttachViewModel();
    }

    public LotteryPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        AttachViewModel();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        if (_isViewModelSubscribed)
        {
            ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
            _isViewModelSubscribed = false;
        }
        DataContext = null;
    }

    private void AttachViewModel()
    {
        DataContext = ViewModel;
        if (_isViewModelSubscribed)
            return;

        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _isViewModelSubscribed = true;
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        Dispatcher.UIThread.Post(async () => await RunResultAnimationAsync(e.PropertyName), DispatcherPriority.Render);
    }

    private async Task RunResultAnimationAsync(string? propertyName)
    {
        if (_isUnloaded)
            return;

        try
        {
            if (propertyName == nameof(LotteryPageViewModel.PreviewAnimationRevision))
            {
                await WaitForResultPresenterLayoutAsync();
                await DrawAnimationHelper.PreviewAsync(
                    _resultPresenter,
                    ViewModel.AnimationStyle,
                    ViewModel.PreviewAnimationDuration);
            }
            else if (propertyName == nameof(LotteryPageViewModel.ResultAnimationRevision))
            {
                await WaitForResultPresenterLayoutAsync();
                await DrawAnimationHelper.RevealAsync(
                    _resultPresenter,
                    ViewModel.AnimationEnabled,
                    ViewModel.AnimationStyle,
                    ViewModel.AnimationDuration);
            }
        }
        catch
        {
            // Result animation must never crash a completed draw.
        }
    }

    private static async Task WaitForResultPresenterLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
        await Task.Delay(16);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
    }

    private async void RemainingListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRemainingList();

        var list = new ListBox
        {
            ItemsSource = ViewModel.RemainingItems,
            ItemTemplate = this.FindResource("LotteryRemainingItemTemplate") as IDataTemplate,
            MinWidth = 420,
            MaxHeight = 520
        };

        await new FAContentDialog
        {
            Title = SR.C_RemainingListTitle,
            Content = list,
            CloseButtonText = SR.C_Close,
            DefaultButton = FAContentDialogButton.Close
        }.ShowAsync(TopLevel.GetTopLevel(this));
    }
}
