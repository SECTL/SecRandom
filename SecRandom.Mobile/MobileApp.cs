using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.ComponentModel;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views;
using SecRandom.Mobile.Services;
using SecRandom.Mobile.Views.Settings;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Shared;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

public sealed class MobileApp : Avalonia.Application
{
    private IHost? _host;
    private MobileRootView? _rootView;
    private bool _stopping;

    public override void Initialize()
    {
        Styles.Add(new FluentAvaloniaTheme
        {
            PreferSystemTheme = true,
            UseSystemFontOnWindows = true
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime singleView)
            throw new PlatformNotSupportedException("SecRandom.Mobile requires an Avalonia single-view lifetime.");

        try
        {
            ConfigureMobileDataRoot();
            _host = Host
                .CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddPlatformServices(PlatformStartupContext.Current);
                    services.AddCoreRuntimeServices();
                    services.AddSingleton<SingleViewHostProvider>();
                    services.AddSingleton<IViewHostProvider>(serviceProvider =>
                        serviceProvider.GetRequiredService<SingleViewHostProvider>());
                    services.AddViewEngine()
                        .AddView<MobileDrawView>("main.rollCall")
                        .AddView<MobileHistoryPage>("main.history")
                        .AddView<MobileOverviewPage>("main.overview")
                        .AddView<MobileSettingsView>("settings.mobile");
                    services.AddHttpClient<MobileUpdateService>();
                    services.AddSingleton<MobileDeviceUuidStore>();
                    services.AddHostedService<MobileOnlineStatusService>();
                    services.AddSingleton<MobileRootView>();
                })
                .Build();
            IAppHost.Host = _host;
            _rootView = _host.Services.GetRequiredService<MobileRootView>();
            singleView.MainView = _rootView;

            if (singleView is IControlledApplicationLifetime controlled)
                controlled.Exit += (_, _) => _ = StopHostAsync();

            _ = StartMobileHostAsync(_host, singleView);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                Android.Util.Log.Error("SecRandom.Mobile", exception.ToString());
#endif
            if (ReferenceEquals(IAppHost.Host, _host))
                IAppHost.Host = null;
            _host?.Dispose();
            _host = null;
            singleView.MainView = new TextBlock
            {
                Text = LR.M_StartupFailed,
                Margin = new Thickness(32),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StopHostAsync()
    {
        var host = _host;
        if (_stopping || host is null)
            return;

        _stopping = true;
        try
        {
            if (_rootView is not null)
            {
                await host.Services.GetRequiredService<IViewEngine>()
                    .CloseHostAsync(_rootView.InnerViewHost, ViewCloseReason.ApplicationShutdown)
                    .ConfigureAwait(false);
                await _rootView.InnerViewHost.DestroyAsync().ConfigureAwait(false);
            }
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            if (ReferenceEquals(IAppHost.Host, host))
                IAppHost.Host = null;
            host.Dispose();
            if (ReferenceEquals(_host, host))
                _host = null;
            _rootView = null;
        }
    }

    private static void ConfigureMobileDataRoot()
    {
        Utils.ConfigureMobileDataRoot();
    }

    private async Task StartMobileHostAsync(IHost host, ISingleViewApplicationLifetime singleView)
    {
        try
        {
            await host.StartAsync().ConfigureAwait(false);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            _ = host.Services.GetRequiredService<IProfileService>();
            _ = host.Services.GetRequiredService<IDrawTemporaryRecordService>();
            _ = host.Services.GetRequiredService<IFeatureAvailabilityService>();
            _ = host.Services.GetRequiredService<DrawEngine>();
            await host.Services.GetRequiredService<IViewEngine>()
                .ShowAsync("main.rollCall")
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(exception);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            await StopHostAsync().ConfigureAwait(false);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                singleView.MainView = new TextBlock
                {
                    Text = LR.M_StartupFailed,
                    Margin = new Thickness(32),
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                };
            }).GetTask();
        }
    }
}

public sealed class MobileRootView : UserControl
{
    private readonly MainConfigHandler _configHandler;
    private readonly ViewHostControl _viewHost;
    private readonly IViewEngine _viewEngine;
    private readonly TextBlock _pageTitle;
    private readonly Button _drawTab;
    private readonly Button _historyTab;
    private readonly Button _overviewTab;
    private readonly Button _settingsTab;
    private readonly Border _header;
    private readonly Border _bottomBar;
    private readonly Grid _root;
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
        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);

        _pageTitle = new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Foreground = MobileTheme.Text,
            VerticalAlignment = VerticalAlignment.Center
        };
        _drawTab = MobileUi.CreateNavigationButton(LR.N_Draw);
        _historyTab = MobileUi.CreateNavigationButton(LR.N_History);
        _overviewTab = MobileUi.CreateNavigationButton(LR.N_Overview);
        _settingsTab = MobileUi.CreateNavigationButton(LR.N_Settings);
        _drawTab.Click += async (_, _) => await ShowDrawAsync();
        _historyTab.Click += async (_, _) => await ShowHistoryAsync();
        _overviewTab.Click += async (_, _) => await ShowOverviewAsync();
        _settingsTab.Click += async (_, _) => await ShowSettingsAsync();

        _header = new Border
        {
            Padding = new Thickness(20, 14),
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                Children =
                {
                    new Image
                    {
                        Source = new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/AppLogo.png"))),
                        Width = 32,
                        Height = 32,
                        Margin = new Thickness(0, 0, 10, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    _pageTitle
                }
            }
        };
        Grid.SetColumn(_pageTitle, 1);

        _bottomBar = new Border
        {
            Padding = new Thickness(8, 8, 8, 10),
            Background = MobileTheme.Surface,
            BorderBrush = MobileTheme.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
                Children = { _drawTab, _historyTab, _overviewTab, _settingsTab }
            }
        };
        Grid.SetColumn(_historyTab, 1);
        Grid.SetColumn(_overviewTab, 2);
        Grid.SetColumn(_settingsTab, 3);

        _root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Background = MobileTheme.Canvas,
            Children = { _header, _viewHost, _bottomBar }
        };
        Grid.SetRow(_viewHost, 1);
        Grid.SetRow(_bottomBar, 2);
        Content = _root;

        AddHandler(TopLevel.BackRequestedEvent, OnBackRequested, RoutingStrategies.Bubble);
        _configHandler.Data.Appearance.PropertyChanged += AppearanceOnPropertyChanged;
        DetachedFromVisualTree += (_, _) => _configHandler.Data.Appearance.PropertyChanged -= AppearanceOnPropertyChanged;
        RenderCurrentDestination();
    }

    private void OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (e.Handled)
            return;

        e.Handled = true;
        _ = _viewHost.RequestBackAsync();
    }

    public ViewHostControl InnerViewHost => _viewHost;

    private async Task ShowHistoryAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.History, "main.history").ConfigureAwait(true);
    }

    private async Task ShowDrawAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Draw, "main.rollCall").ConfigureAwait(true);
    }

    private async Task ShowSettingsAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Settings, "settings.mobile").ConfigureAwait(true);
    }

    private async Task ShowOverviewAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Overview, "main.overview").ConfigureAwait(true);
    }

    private async Task ShowPrimaryRouteAsync(MobileDestination destination, string routeId)
    {
        foreach (var activeRoute in PrimaryRouteIds)
        {
            if (!string.Equals(activeRoute, routeId, StringComparison.Ordinal))
                await _viewEngine.CloseAsync(activeRoute).ConfigureAwait(true);
        }

        _destination = destination;
        RenderCurrentDestination();
        await _viewEngine.ShowAsync(routeId).ConfigureAwait(true);
    }

    private void RenderCurrentDestination()
    {
        _drawTab.Foreground = _destination == MobileDestination.Draw ? MobileTheme.Primary : MobileTheme.MutedText;
        _historyTab.Foreground = _destination == MobileDestination.History ? MobileTheme.Primary : MobileTheme.MutedText;
        _overviewTab.Foreground = _destination == MobileDestination.Overview ? MobileTheme.Primary : MobileTheme.MutedText;
        _settingsTab.Foreground = _destination == MobileDestination.Settings ? MobileTheme.Primary : MobileTheme.MutedText;
        _pageTitle.Text = _destination switch
        {
            MobileDestination.Draw => LR.P_Draw,
            MobileDestination.History => LR.P_History,
            MobileDestination.Overview => LR.P_Overview,
            MobileDestination.Settings => LR.P_Settings,
            _ => throw new ArgumentOutOfRangeException()
        };

    }

    private void AppearanceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(_configHandler.Data.Appearance.Theme))
            return;

        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);
        _root.Background = MobileTheme.Canvas;
        _header.Background = MobileTheme.Surface;
        _header.BorderBrush = MobileTheme.Border;
        _bottomBar.Background = MobileTheme.Surface;
        _bottomBar.BorderBrush = MobileTheme.Border;
        _pageTitle.Foreground = MobileTheme.Text;
        RenderCurrentDestination();
    }

    private static IReadOnlyList<string> PrimaryRouteIds { get; } =
        ["main.rollCall", "main.history", "main.overview", "settings.mobile"];

}
