using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Templates;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.ViewModels;
using SR = SecRandom.Langs.MainPages.RollCall.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.rollCall", "\uECAA", location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class RollCallPage : UserControl
{
    public RollCallPage()
    {
        ViewModel = IAppHost.GetService<RollCallPageViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public RollCallPageViewModel ViewModel { get; }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        ViewModel.Dispose();
        DataContext = null;
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
