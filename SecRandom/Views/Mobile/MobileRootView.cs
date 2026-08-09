using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Controls;
using SecRandom.Core.Views;
using SecRandom.Services.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileRootView : ViewBase, IFANavigationPageFactory
{
    private readonly IServiceProvider _services;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly ILogger<MobileRootView>? _logger;
    private readonly TabStrip _bottomNavigation;
    private readonly FAFrame _pageOutlet;
    private bool _isAdornerAdded;
    private bool _synchronizingBottomNavigation;
    private MobileDestination _destination = MobileDestination.Draw;

    public MobileRootView(IServiceProvider services,
        IMobileSettingsNavigator settingsNavigator, ILogger<MobileRootView>? logger = null)
    {
        _services = services;
        _settingsNavigator = settingsNavigator;
        _logger = logger;
        InitializeComponent();
        _bottomNavigation = this.FindControl<TabStrip>(@"BottomNavigation")!;
        _pageOutlet = this.FindControl<FAFrame>(@"PageOutlet")!;
        _pageOutlet.NavigationPageFactory = this;
        NavigateRoot(MobileDestination.Draw);
        UpdateDestinationChrome();
    }

    public static string HeaderText => LR.P_Draw;

    public Control? GetPage(Type srcType) => Activator.CreateInstance(srcType) as Control;

    public Control? GetPageFromObject(object target)
    {
        if (target is not string pageId)
            return null;

        return _services.GetKeyedService<UserControl>(pageId);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!GlobalConstants.IsDevelopment || _isAdornerAdded || Content is not Control element)
            return;

        var layer = AdornerLayer.GetAdornerLayer(element);
        if (layer is null)
            return;

        var adorner = new DevelopmentBuildAdorner();
        layer.Children.Add(adorner);
        AdornerLayer.SetAdornedElement(adorner, this);
        _isAdornerAdded = true;
    }

    private async void BottomNavigation_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_synchronizingBottomNavigation || e.Source != _bottomNavigation ||
            !TryGetDestination(_bottomNavigation.SelectedIndex, out var destination))
            return;

        try
        {
            if (destination == MobileDestination.Settings)
            {
                await _settingsNavigator.OpenAsync();
                SynchronizeBottomNavigation();
                return;
            }

            if (_settingsNavigator.IsOpen)
                await _settingsNavigator.CloseAsync();
            if (!NavigateRoot(destination))
                SynchronizeBottomNavigation();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "无法打开移动端设置界面。");
            await new FAContentDialog
            {
                Title = LR.M_OpenSettingsFailed,
                Content = exception.Message,
                CloseButtonText = LR.C_Close,
                DefaultButton = FAContentDialogButton.Close
            }.ShowAsync(TopLevel.GetTopLevel(this));
            SynchronizeBottomNavigation();
        }
    }

    private void UpdateDestinationChrome()
    {
        SynchronizeBottomNavigation();
        Header = _destination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            _ => LR.P_Draw
        };
    }

    private void SynchronizeBottomNavigation()
    {
        var selectedIndex = GetDestinationIndex(_destination);
        if (_bottomNavigation.SelectedIndex == selectedIndex)
            return;

        _synchronizingBottomNavigation = true;
        try
        {
            _bottomNavigation.SelectedIndex = selectedIndex;
        }
        finally
        {
            _synchronizingBottomNavigation = false;
        }
    }

    private static bool TryGetDestination(int selectedIndex, out MobileDestination destination)
    {
        destination = selectedIndex switch
        {
            0 => MobileDestination.Draw,
            1 => MobileDestination.History,
            2 => MobileDestination.Overview,
            3 => MobileDestination.Settings,
            _ => default
        };
        return selectedIndex is >= 0 and <= 3;
    }

    private static int GetDestinationIndex(MobileDestination destination) => destination switch
    {
        MobileDestination.Draw => 0,
        MobileDestination.History => 1,
        MobileDestination.Overview => 2,
        MobileDestination.Settings => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    };

    private bool NavigateRoot(MobileDestination destination)
    {
        if (destination == MobileDestination.Settings)
            return false;

        _destination = destination;
        _pageOutlet.NavigateFromObject(GetRootPageId(destination));
        UpdateDestinationChrome();
        return true;
    }

    private static string GetRootPageId(MobileDestination destination) => destination switch
    {
        MobileDestination.Draw => MobilePageIds.Draw,
        MobileDestination.History => MobilePageIds.History,
        MobileDestination.Overview => MobilePageIds.Overview,
        _ => throw new ArgumentOutOfRangeException(nameof(destination))
    };

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
