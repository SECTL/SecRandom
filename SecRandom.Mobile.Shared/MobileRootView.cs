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
    private readonly ViewHostControl _modalViewHost;
    private readonly SingleViewHostProvider _singleViewHostProvider;
    private readonly IMobileNavigator _navigator;
    private readonly MobilePageHeader _header;
    private readonly MobileNavigationBar _bottomBar;
    private readonly ContentControl _pageOutlet;

    public MobileRootView(
        MainConfigHandler configHandler,
        SingleViewHostProvider singleViewHostProvider,
        IMobileNavigator navigator)
    {
        _configHandler = configHandler;
        _singleViewHostProvider = singleViewHostProvider;
        _navigator = navigator;
        _modalViewHost = new ViewHostControl("mobile.modal");
        _singleViewHostProvider.Attach(_modalViewHost);

        InitializeComponent();
        _header = this.FindControl<MobilePageHeader>("PageHeader")!;
        _bottomBar = this.FindControl<MobileNavigationBar>("BottomBar")!;
        _bottomBar.DestinationSelected += BottomBarOnDestinationSelected;
        _pageOutlet = this.FindControl<ContentControl>("PageOutlet")!;
        _navigator.Attach(_pageOutlet);
        var root = this.FindControl<Grid>("RootLayout")!;
        root.Children.Add(_modalViewHost);
        Grid.SetRow(_modalViewHost, 1);
        Grid.SetRowSpan(_modalViewHost, 3);
        _modalViewHost.ZIndex = 1;

        AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
        _configHandler.Data.Appearance.PropertyChanged += AppearanceOnPropertyChanged;
        DetachedFromVisualTree += (_, _) => _configHandler.Data.Appearance.PropertyChanged -= AppearanceOnPropertyChanged;
        _navigator.DestinationChanged += NavigatorOnDestinationChanged;
        ApplyTheme();
        RenderCurrentDestination();
    }

    public ViewHostControl ModalViewHost => _modalViewHost;

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
        _singleViewHostProvider.Detach(_modalViewHost);
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled)
            return;

        e.Handled = true;
        if (_modalViewHost.ActiveModalView is not null)
        {
            _ = _modalViewHost.CloseActiveViewAsync();
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
            MobileDestination.Settings => LR.P_Settings,
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
            if (!await _navigator.NavigateRootAsync(destination).ConfigureAwait(true))
                _bottomBar.Select(_navigator.CurrentDestination);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            _bottomBar.Select(_navigator.CurrentDestination);
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
