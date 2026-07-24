using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Controls;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using SecRandom.Services.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileRootView : ViewBase
{
    private readonly MainConfigHandler _configHandler;
    private readonly IMobileNavigator _navigator;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly ILogger<MobileRootView>? _logger;
    private readonly TabStrip _bottomNavigation;
    private readonly ContentControl _pageOutlet;
    private bool _isAdornerAdded;
    private bool _synchronizingBottomNavigation;

    public MobileRootView(MainConfigHandler configHandler, IMobileNavigator navigator,
        IMobileSettingsNavigator settingsNavigator, ILogger<MobileRootView>? logger = null)
    {
        _configHandler = configHandler;
        _navigator = navigator;
        _settingsNavigator = settingsNavigator;
        _logger = logger;
        InitializeComponent();
        _bottomNavigation = this.FindControl<TabStrip>("BottomNavigation")!;
        _pageOutlet = this.FindControl<ContentControl>("PageOutlet")!;
        _navigator.Attach(_pageOutlet);
        _navigator.DestinationChanged += NavigatorOnDestinationChanged;
        _configHandler.Data.Appearance.PropertyChanged += AppearanceOnPropertyChanged;
        Closed += (_, _) =>
        {
            _navigator.DestinationChanged -= NavigatorOnDestinationChanged;
            _navigator.Detach(_pageOutlet);
            _configHandler.Data.Appearance.PropertyChanged -= AppearanceOnPropertyChanged;
        };
        ApplyTheme();
        UpdateDestinationChrome();
    }

    public static string HeaderText => LR.P_Draw;

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
            if (!await _navigator.NavigateRootAsync(destination))
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

    private void NavigatorOnDestinationChanged(object? sender, EventArgs e) => UpdateDestinationChrome();

    private void UpdateDestinationChrome()
    {
        SynchronizeBottomNavigation();
        Header = _navigator.CurrentDestination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            _ => LR.P_Draw
        };
    }

    private void SynchronizeBottomNavigation()
    {
        var selectedIndex = GetDestinationIndex(_navigator.CurrentDestination);
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

    private void AppearanceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_configHandler.Data.Appearance.Theme))
            ApplyTheme();
    }

    private void ApplyTheme()
    {
        Application.Current!.RequestedThemeVariant = _configHandler.Data.Appearance.Theme switch
        {
            SecRandom.Core.Enums.Configs.ThemeMode.Light => Avalonia.Styling.ThemeVariant.Light,
            SecRandom.Core.Enums.Configs.ThemeMode.Dark => Avalonia.Styling.ThemeVariant.Dark,
            _ => Avalonia.Styling.ThemeVariant.Default
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
