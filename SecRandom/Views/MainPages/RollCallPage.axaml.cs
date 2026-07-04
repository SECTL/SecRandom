using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Templates;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Helpers;
using SecRandom.ViewModels;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.rollCall", FluentIcons.PeopleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class RollCallPage : UserControl
{
    private bool _isUnloaded;

    public RollCallPage()
    {
        ViewModel = IAppHost.GetService<RollCallPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
    }

    public RollCallPageViewModel ViewModel { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        ViewModel.Dispose();
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        DataContext = null;
    }

    private async void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isUnloaded)
            return;

        try
        {
            if (e.PropertyName == nameof(RollCallPageViewModel.PreviewAnimationRevision))
            {
                await DrawResultAnimationHelper.PreviewSwapAsync(
                    ResultPresenter,
                    ViewModel.PreviewAnimationDuration);
            }
            else if (e.PropertyName == nameof(RollCallPageViewModel.ResultAnimationRevision))
            {
                await DrawResultAnimationHelper.RevealAsync(
                    ResultPresenter,
                    ViewModel.ResultFlowAnimationEnabled,
                    ViewModel.ResultFlowAnimationDuration);
            }
        }
        catch
        {
            // Result animation must never crash a completed draw.
        }
    }

    private async void RemainingListButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ViewModel.RefreshRemainingList();

        var list = new ListBox
        {
            ItemsSource = ViewModel.RemainingItems,
            ItemTemplate = this.FindResource("RollCallRemainingItemTemplate") as IDataTemplate,
            MinWidth = 360,
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
