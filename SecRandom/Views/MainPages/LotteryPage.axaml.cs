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
using SecRandom.Core.Views;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.Lottery.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.lottery", FluentIcons.GiftFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class LotteryPage : ViewBase
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
        Closed += OnViewClosed;
    }

    public LotteryPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        AttachViewModel();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => DetachViewModel();

    // ViewModel 是单例且被快捷键/协议等 UI 无关路径复用，视图关闭只做渲染层解绑，
    // 绝不能在这里 Dispose ViewModel 或中断进行中的抽取（后台停止由 Shortcut/Protocol 负责）。
    private void OnViewClosed(object? sender, ViewClosedEventArgs e) => DetachViewModel();

    private void DetachViewModel()
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

    private void RemainingListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRemainingList();
        var window = new RemainingListWindow(
            SR.C_RemainingListTitle,
            ViewModel.RemainingItems,
            this.FindResource("LotteryRemainingItemTemplate") as IDataTemplate);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            window.Show();
        else
            window.Show(owner);
    }
}
