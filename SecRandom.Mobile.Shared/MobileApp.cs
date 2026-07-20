using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System.ComponentModel;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Models;
using SecRandom.Core.Views;
using System.Globalization;
using System.Text.Json;
using SecRandom.Mobile.Views;
using SecRandom.Mobile.Services;
using SecRandom.Mobile.Views.Settings;
using SecRandom.Mobile.Controls;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Telemetry;
using SecRandom.Shared;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;
using AvaloniaButton = Avalonia.Controls.Button;

namespace SecRandom.Mobile;

public sealed partial class MobileApp : Avalonia.Application
{
    private IHost? _host;
    private MobileRootView? _rootView;
    private ISingleViewApplicationLifetime? _singleView;
    private bool _stopping;
    private bool _startupFailureShown;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime singleView)
            throw new PlatformNotSupportedException("SecRandom.Mobile requires an Avalonia single-view lifetime.");

        _singleView = singleView;
        Dispatcher.UIThread.UnhandledException += DispatcherOnUnhandledException;
        try
        {
            ConfigureMobileDataRoot();
            // Host 构建前的最小 culture 初始化：此时 MainConfigHandler 尚未由容器构造，
            // 与桌面 Initialize 等价地直接读取 settings.json，必须早于任何视图创建。
            InitializeMobileLanguage();
            _host = Host
                .CreateDefaultBuilder()
                .ConfigureServices(services =>
                {
                    services.AddPlatformServices(PlatformStartupContext.Current);
                    services.AddCoreRuntimeServices();
                    services.AddSingleton<ITelemetrySdkAdapter, SentryTelemetrySdkAdapter>();
                    services.AddSingleton<TelemetryRuntimeService>();
                    services.AddTransient<MobileRollCallService>();
                    services.AddSingleton<MobileMediaLibraryService>();
                    services.AddSingleton<IMobileMediaPlayer>(
                        (PlatformStartupContext.Current as MobilePlatformServiceRoot)?.MediaPlayer
                        ?? new UnsupportedMobileMediaPlayer());
                    services.AddSingleton<MobileDrawMediaService>();
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
                    services.AddSingleton<IMobileUpdateInstaller>(
                        (PlatformStartupContext.Current as MobilePlatformServiceRoot)?.UpdateInstaller
                        ?? new UnsupportedMobileUpdateInstaller());
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
            ReportException(exception);
            if (ReferenceEquals(IAppHost.Host, _host))
                IAppHost.Host = null;
            _host?.Dispose();
            _host = null;
            ShowStartupFailure(exception);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private void DispatcherOnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportException(e.Exception);
        e.Handled = true;
        ShowStartupFailure(e.Exception);
    }

    private void ReportException(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(exception);
        (PlatformStartupContext.Current as MobilePlatformServiceRoot)?.StartupErrorLogger?.Invoke(exception);
    }

    private void ShowStartupFailure(Exception exception)
    {
        if (_startupFailureShown || _singleView is null)
            return;

        _startupFailureShown = true;
        string startupFailedText;
        try
        {
            startupFailedText = LR.M_StartupFailed;
        }
        catch (Exception resourceException)
        {
            startupFailedText = "SecRandom startup failed: " + resourceException.GetType().Name;
        }

        _singleView.MainView = new ScrollViewer
        {
            Content = new TextBlock
            {
                Text = startupFailedText + "\n" + exception,
                Margin = new Thickness(32),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
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
            var mediaPlayer = host.Services.GetRequiredService<IMobileMediaPlayer>();
            await mediaPlayer.StopAsync().ConfigureAwait(false);
            if (mediaPlayer is IDisposable disposableMediaPlayer)
                disposableMediaPlayer.Dispose();
            await host.Services.GetRequiredService<TelemetryRuntimeService>()
                .ShutdownAsync()
                .ConfigureAwait(false);
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportException(exception);
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

    /// <summary>
    /// 与桌面 LoadStartupSettings 等价的最小读取路径：MainConfigModel 自带 legacy basic
    /// 桥接，直接反序列化 settings.json 即可同时覆盖 canonical general 与旧版 basic 布局。
    /// </summary>
    private static void InitializeMobileLanguage()
    {
        var mainConfig = new MainConfigModel();
        if (File.Exists(mainConfig.ConfigFilePath))
        {
            try
            {
                mainConfig = JsonSerializer.Deserialize<MainConfigModel>(
                    File.ReadAllText(mainConfig.ConfigFilePath),
                    ConfigServiceBase.JsonOptions) ?? mainConfig;
            }
            catch
            {
                // 配置损坏时退回默认语言，不阻塞启动。
            }
        }

        ApplyMobileCulture(mainConfig.General.Basic.Language);
    }

    /// <summary>
    /// 与桌面 InitializeLanguages 对齐：四项 culture + 移动端资源类的显式 Culture。
    /// 语言设置页切换后也会调用此方法，再重建根视图完成免重启刷新。
    /// </summary>
    internal static void ApplyMobileCulture(LanguageMode mode)
    {
        var cultureInfo = new CultureInfo(mode switch
        {
            LanguageMode.ChineseSimplified => @"zh-Hans",
            LanguageMode.English => @"en-US",
            LanguageMode.Japanese => @"ja-JP",
            _ => @"zh-Hans"
        });
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        Langs.Mobile.Resources.Culture = cultureInfo;
    }

    /// <summary>
    /// 语言切换的免重启重建：先关闭并销毁当前根视图的 host 会话（参考 StopHostAsync 的
    /// 清理顺序），再替换 SingleView 主视图为新的 <see cref="MobileRootView"/> 并恢复默认
    /// 抽取路由。移动页面全是命令式 Render，重建根视图即全面刷新。DI 中 MobileRootView
    /// 是单例，这里用 ActivatorUtilities 显式构造新实例，保证会话状态干净。
    /// </summary>
    internal async Task ReloadRootViewAsync()
    {
        var host = _host;
        var singleView = _singleView;
        if (_stopping || host is null || singleView is null)
            return;

        var viewEngine = host.Services.GetRequiredService<IViewEngine>();
        var oldRoot = _rootView;
        if (oldRoot is not null)
        {
            await viewEngine.CloseHostAsync(oldRoot.InnerViewHost, ViewCloseReason.Programmatic)
                .ConfigureAwait(false);
            await oldRoot.InnerViewHost.DestroyAsync().ConfigureAwait(false);
            host.Services.GetRequiredService<SingleViewHostProvider>().Detach(oldRoot.InnerViewHost);
        }

        var newRoot = ActivatorUtilities.CreateInstance<MobileRootView>(host.Services);
        _rootView = newRoot;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => singleView.MainView = newRoot);
        await viewEngine.ShowAsync(MobileRoutes.Draw).ConfigureAwait(false);
    }

    private async Task StartMobileHostAsync(IHost host, ISingleViewApplicationLifetime singleView)
    {
        try
        {
            _ = host.Services.GetRequiredService<IProfileService>();
            _ = host.Services.GetRequiredService<IDrawTemporaryRecordService>();
            _ = host.Services.GetRequiredService<IFeatureAvailabilityService>();
            _ = host.Services.GetRequiredService<DrawEngine>();
            await host.Services.GetRequiredService<IViewEngine>()
                .ShowAsync(MobileRoutes.Draw)
                .ConfigureAwait(false);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            await host.StartAsync().ConfigureAwait(false);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            await host.Services.GetRequiredService<TelemetryRuntimeService>()
                .InitializeAsync()
                .ConfigureAwait(false);
            if (_stopping || !ReferenceEquals(host, _host))
                return;
        }
        catch (Exception exception)
        {
            ReportException(exception);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            try
            {
                await StopHostAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                ReportException(cleanupException);
            }

            await Dispatcher.UIThread.InvokeAsync(() => ShowStartupFailure(exception)).GetTask();
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
        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);

        _header = new MobilePageHeader();
        _bottomBar = new MobileNavigationBar();
        _bottomBar.DestinationSelected += BottomBarOnDestinationSelected;

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
        await _navigationGate.WaitAsync().ConfigureAwait(true);
        var previousDestination = _destination;
        var previousRoute = GetRouteId(previousDestination);
        try
        {
            foreach (var activeRoute in PrimaryRouteIds)
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
        if (e.PropertyName != nameof(_configHandler.Data.Appearance.Theme))
            return;

        MobileTheme.Apply(_configHandler.Data.Appearance.Theme);
        _root.Background = MobileTheme.Canvas;
        _header.RefreshTheme();
        _bottomBar.RefreshTheme();
        RenderCurrentDestination();
    }

    private async void BottomBarOnDestinationSelected(object? sender, MobileDestination destination)
    {
        try
        {
            await ShowPrimaryRouteAsync(destination, destination switch
            {
                MobileDestination.Draw => MobileRoutes.Draw,
                MobileDestination.History => MobileRoutes.History,
                MobileDestination.Overview => MobileRoutes.Overview,
                MobileDestination.Settings => MobileRoutes.Settings,
                _ => MobileRoutes.Draw
            }).ConfigureAwait(true);
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

    private static IReadOnlyList<string> PrimaryRouteIds => MobileRoutes.All;

}
