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
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Controls;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Mobile.Views;
using SecRandom.Mobile.Services;
using SecRandom.Mobile.Views.Settings;
using SecRandom.Mobile.Controls;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Shared;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

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
                        .AddView<MobileDrawPage>(MobileRoutes.Draw)
                        .AddView<MobileHistoryPage>(MobileRoutes.History)
                        .AddView<MobileOverviewPage>(MobileRoutes.Overview)
                        .AddView<MobileSettingsCatalogPage>(MobileRoutes.Settings)
                        .AddView<MobileGeneralSettingsPage>(MobileRoutes.General)
                        .AddView<MobilePersonalizationSettingsPage>(MobileRoutes.Personalization)
                        .AddView<MobileListManagementSettingsPage>(MobileRoutes.ListManagement)
                        .AddView<MobileDrawSettingsPage>(MobileRoutes.DrawSettings)
                        .AddView<MobileBackupSettingsPage>(MobileRoutes.Backup)
                        .AddView<MobileUpdateSettingsPage>(MobileRoutes.Update)
                        .AddView<MobileAboutSettingsPage>(MobileRoutes.About);
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
                .ShowAsync(MobileRoutes.Draw)
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
    private readonly MobilePageHeader _header;
    private readonly MobileNavigationBar _bottomBar;
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

        _header = new MobilePageHeader();
        _bottomBar = new MobileNavigationBar();
        _bottomBar.NavigationView.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is FANavigationViewItem { Tag: MobileDestination destination })
                _ = ShowPrimaryRouteAsync(destination, destination switch
                {
                    MobileDestination.Draw => MobileRoutes.Draw,
                    MobileDestination.History => MobileRoutes.History,
                    MobileDestination.Overview => MobileRoutes.Overview,
                    MobileDestination.Settings => MobileRoutes.Settings,
                    _ => MobileRoutes.Draw
                });
        };

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
        await ShowPrimaryRouteAsync(MobileDestination.History, MobileRoutes.History).ConfigureAwait(true);
    }

    private async Task ShowDrawAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Draw, MobileRoutes.Draw).ConfigureAwait(true);
    }

    private async Task ShowSettingsAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Settings, MobileRoutes.Settings).ConfigureAwait(true);
    }

    private async Task ShowOverviewAsync()
    {
        await ShowPrimaryRouteAsync(MobileDestination.Overview, MobileRoutes.Overview).ConfigureAwait(true);
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
        if (e.PropertyName != nameof(_configHandler.Data.Appearance.Theme))
            return;

        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);
        _root.Background = MobileTheme.Canvas;
        _header.RefreshTheme();
        _bottomBar.RefreshTheme();
        RenderCurrentDestination();
    }

    private static IReadOnlyList<string> PrimaryRouteIds => MobileRoutes.All;

}
