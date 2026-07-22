using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using SecRandom.Controls.Mobile;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using SecRandom.Services.Mobile;
using LR = SecRandom.Langs.Mobile.Resources;

namespace SecRandom.Views.Mobile;

public sealed partial class MobileRootView : UserControl
{
    private readonly MainConfigHandler _configHandler;
    private readonly ViewHostControl _viewHost;
    private readonly SingleViewHostProvider _singleViewHostProvider;
    private readonly IMobileNavigator _navigator;
    private readonly IMobileSettingsNavigator _settingsNavigator;
    private readonly MobilePageHeader _header;
    private readonly MobileNavigationBar _bottomBar;
    private readonly ContentControl _pageOutlet;
    private readonly ILogger<MobileRootView>? _logger;

    public MobileRootView(
        MainConfigHandler configHandler,
        SingleViewHostProvider singleViewHostProvider,
        IMobileNavigator navigator,
        IMobileSettingsNavigator settingsNavigator,
        ILogger<MobileRootView>? logger = null)
    {
        _configHandler = configHandler;
        _singleViewHostProvider = singleViewHostProvider;
        _navigator = navigator;
        _settingsNavigator = settingsNavigator;
        _logger = logger;
        _viewHost = new ViewHostControl("mobile.root");
        _singleViewHostProvider.Attach(_viewHost);

        InitializeComponent();
        _header = this.FindControl<MobilePageHeader>("PageHeader")!;
        _bottomBar = this.FindControl<MobileNavigationBar>("BottomBar")!;
        _bottomBar.DestinationSelected += BottomBarOnDestinationSelected;
        _pageOutlet = this.FindControl<ContentControl>("PageOutlet")!;
        _navigator.Attach(_pageOutlet);
        var root = this.FindControl<Grid>("RootLayout")!;
        root.Children.Add(_viewHost);
        Grid.SetRow(_viewHost, 0);
        Grid.SetRowSpan(_viewHost, 3);
        _viewHost.ZIndex = 1;

        AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
        _configHandler.Data.Appearance.PropertyChanged += AppearanceOnPropertyChanged;
        DetachedFromVisualTree += (_, _) => _configHandler.Data.Appearance.PropertyChanged -= AppearanceOnPropertyChanged;
        _navigator.DestinationChanged += NavigatorOnDestinationChanged;
        ApplyTheme();
        RenderCurrentDestination();
    }

    public ViewHostControl ViewHost => _viewHost;

    public Task ResetNavigationAsync() => _navigator.ResetToDrawAsync();

    public Task DetachAsync()
    {
        if (Application.Current is null || Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            DetachCore();
            return Task.CompletedTask;
        }

        return Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(DetachCore).GetTask();
    }

    private void DetachCore()
    {
        _navigator.DestinationChanged -= NavigatorOnDestinationChanged;
        _navigator.Detach(_pageOutlet);
        _singleViewHostProvider.Detach(_viewHost);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled)
            return;

        e.Handled = true;
        if (_viewHost.HasActiveView)
        {
            _ = _viewHost.CloseActiveViewAsync();
            return;
        }

        _ = _navigator.GoBackAsync();
    }

    private void RenderCurrentDestination()
    {
        _header.Title = _navigator.CurrentDestination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            _ => throw new ArgumentOutOfRangeException()
        };
        _bottomBar.Select(_navigator.CurrentDestination);
    }

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

    private void NavigatorOnDestinationChanged(object? sender, EventArgs e) => RenderCurrentDestination();

    private async void BottomBarOnDestinationSelected(object? sender, MobileDestination destination)
    {
        try
        {
            if (destination == MobileDestination.Settings)
            {
                await _settingsNavigator.OpenAsync().ConfigureAwait(true);
                _bottomBar.Select(_navigator.CurrentDestination);
                return;
            }

            var wasSettingsOpen = _settingsNavigator.IsOpen;
            if (wasSettingsOpen)
                await _settingsNavigator.CloseAsync().ConfigureAwait(true);

            if (destination == _navigator.CurrentDestination)
            {
                _bottomBar.Select(_navigator.CurrentDestination);
                return;
            }

            if (!await _navigator.NavigateRootAsync(destination).ConfigureAwait(true))
                _bottomBar.Select(_navigator.CurrentDestination);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "无法打开移动端设置界面。");
            await ShowOpenSettingsErrorAsync(exception);
            _bottomBar.Select(_navigator.CurrentDestination);
        }
    }

    private Task ShowOpenSettingsErrorAsync(Exception exception) => new FAContentDialog
    {
        Title = LR.M_OpenSettingsFailed,
        Content = new TextBlock
        {
            Text = exception.Message,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        },
        CloseButtonText = LR.C_Close,
        DefaultButton = FAContentDialogButton.Close
    }.ShowAsync(TopLevel.GetTopLevel(this));

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
