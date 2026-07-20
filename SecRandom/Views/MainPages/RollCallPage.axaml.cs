using System.Threading.Tasks;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Helpers;
using SecRandom.ViewModels.MainPages;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.rollCall", FluentIcons.PeopleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class RollCallPage : UserControl
{
    private bool _isUnloaded;
    private bool _isViewModelSubscribed;
    private readonly ItemsControl? _resultPresenter;

    public RollCallPage()
    {
        ViewModel = IAppHost.GetService<RollCallPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        _resultPresenter = this.FindControl<ItemsControl>("ResultPresenter");
        AttachViewModel();
    }

    public RollCallPageViewModel ViewModel { get; }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = false;
        AttachViewModel();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e) => DetachViewModel();

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
            if (propertyName == nameof(RollCallPageViewModel.PreviewAnimationRevision))
            {
                await WaitForResultPresenterLayoutAsync();
                await DrawAnimationHelper.PreviewAsync(
                    _resultPresenter,
                    ViewModel.AnimationStyle,
                    ViewModel.PreviewAnimationDuration);
            }
            else if (propertyName == nameof(RollCallPageViewModel.ResultAnimationRevision))
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
            this.FindResource("RollCallRemainingItemTemplate") as IDataTemplate);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null)
            window.Show();
        else
            window.Show(owner);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
