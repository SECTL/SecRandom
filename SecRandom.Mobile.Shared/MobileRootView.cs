using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Views;
using SecRandom.Mobile.Controls;
using SecRandom.Mobile.Views;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

public sealed partial class MobileRootView : UserControl
{
    private readonly MainConfigHandler _configHandler;
    private readonly ViewHostControl _viewHost;
    private readonly IViewEngine _viewEngine;
    private readonly MobilePageHeader _header;
    private readonly MobileNavigationBar _bottomBar;
    private readonly Grid _root;
    private readonly SemaphoreSlim _navigationGate = new(1, 1);
    private MobileDestination _destination = MobileDestination.Draw;

    public MobileRootView(
        MainConfigHandler configHandler,
        SingleViewHostProvider singleViewHostProvider,
        IViewEngine viewEngine)
    {
        _configHandler = configHandler;
        _viewEngine = viewEngine;
        _viewHost = new ViewHostControl("mobile.root");
        singleViewHostProvider.Attach(_viewHost);

        InitializeComponent();
        _header = this.FindControl<MobilePageHeader>("PageHeader")!;
        _bottomBar = this.FindControl<MobileNavigationBar>("BottomBar")!;
        _bottomBar.DestinationSelected += BottomBarOnDestinationSelected;
        _root = this.FindControl<Grid>("RootLayout")!;
        _root.Children.Add(_viewHost);
        Grid.SetRow(_viewHost, 1);

        AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
        _configHandler.Data.Appearance.PropertyChanged += AppearanceOnPropertyChanged;
        DetachedFromVisualTree += (_, _) => _configHandler.Data.Appearance.PropertyChanged -= AppearanceOnPropertyChanged;
        ApplyTheme();
        RenderCurrentDestination();
    }

    public ViewHostControl InnerViewHost => _viewHost;

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled)
            return;

        e.Handled = true;
        _ = _viewHost.RequestBackAsync();
    }

    private async Task ShowPrimaryRouteAsync(MobileDestination destination, string routeId)
    {
        await _navigationGate.WaitAsync().ConfigureAwait(true);
        MobileDestination previousDestination = _destination;
        string previousRoute = GetRouteId(previousDestination);
        try
        {
            foreach (string activeRoute in MobileRoutes.All)
            {
                if (!string.Equals(activeRoute, routeId, StringComparison.Ordinal))
                    await _viewEngine.CloseAsync(activeRoute).ConfigureAwait(true);
            }

            await _viewEngine.ShowAsync(routeId).ConfigureAwait(true);
            _destination = destination;
            RenderCurrentDestination();
        }
        catch
        {
            if (!string.Equals(previousRoute, routeId, StringComparison.Ordinal))
            {
                try
                {
                    await _viewEngine.ShowAsync(previousRoute).ConfigureAwait(true);
                }
                catch (Exception restoreException)
                {
                    System.Diagnostics.Debug.WriteLine(restoreException);
                }
            }

            _destination = previousDestination;
            RenderCurrentDestination();
            throw;
        }
        finally
        {
            _navigationGate.Release();
        }
    }

    private void RenderCurrentDestination()
    {
        _header.Title = _destination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            MobileDestination.Settings => LR.P_Settings,
            _ => throw new ArgumentOutOfRangeException()
        };
        _bottomBar.Select(_destination);
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

    private async void BottomBarOnDestinationSelected(object? sender, MobileDestination destination)
    {
        try
        {
            await ShowPrimaryRouteAsync(destination, GetRouteId(destination)).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            _bottomBar.Select(_destination);
        }
    }

    private static string GetRouteId(MobileDestination destination) => destination switch
    {
        MobileDestination.Draw => MobileRoutes.Draw,
        MobileDestination.History => MobileRoutes.History,
        MobileDestination.Overview => MobileRoutes.Overview,
        MobileDestination.Settings => MobileRoutes.Settings,
        _ => MobileRoutes.Draw
    };

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
