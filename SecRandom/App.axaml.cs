using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using HotAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Sentry;
using SecRandom.Controls.AttachedSettings;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Icons;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Verification;
using SecRandom.Core.Services.Logging;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Core.Views;
using SecRandom.Shared.Models.Ipc;
using SecRandom.Shared;
using SecRandom.Dialogs;
using AppearanceSettingsConfig = SecRandom.Core.Models.SubConfigs.Personalized.AppearanceSettingsConfig;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Desktop;
using SecRandom.Services.Draw;
using SecRandom.Services.Notification;
using SecRandom.Services.Profiles;
using SecRandom.Services.Ipc;
using SecRandom.Services.ImportExport;
using SecRandom.Services.FirstRun;
using SecRandom.Services.Feedback;
using SecRandom.Services.Linkage;
using SecRandom.Services.Music;
using SecRandom.Services.Settings;
using SecRandom.Services.Security;
using SecRandom.Services.Telemetry;
using SecRandom.Services.Verification;
using SecRandom.Services.Voice;
using SecRandom.Services.Updates;
using SecRandom.Services.ViewEngine;
using SecRandom.Mobile;
using SecRandom.Services.Mobile;
using SecRandom.Views.Mobile;
using SecRandom.Views.Mobile.Settings;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using MobileResources = SecRandom.Langs.Mobile.Resources;
using SecRandom.ViewModels;
using SecRandom.ViewModels.Mobile;
using SecRandom.ViewModels.MainPages;
using SecRandom.ViewModels.SettingsPages;
using SecRandom.ViewModels.SettingsPages.History;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using SecRandom.Views.SettingsPages;
using SecRandom.Views.SettingsPages.About;
using SecRandom.Views.SettingsPages.General;
using SecRandom.Views.SettingsPages.History;
using SecRandom.Views.SettingsPages.Linkage;
using SecRandom.Views.SettingsPages.ListManagement;
using SecRandom.Views.SettingsPages.LogViewer;
using SecRandom.Views.SettingsPages.More;
using SecRandom.Views.SettingsPages.Personalized;
using SecRandom.Views.SettingsPages.Picking;
// using SecRandom.Views.SettingsPages.Plugins.Overview;
using SecRandom.Views.SettingsPages.Update;
using DefaultNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.DefaultNotificationSettingsPage;
using FloatingWindowSettingsPage = SecRandom.Views.SettingsPages.Personalized.FloatingWindowSettingsPage;
using LotteryNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.LotteryNotificationSettingsPage;
using QuickDrawNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.QuickDrawNotificationSettingsPage;
using RollCallNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.RollCallNotificationSettingsPage;
using SecuritySettingsPage = SecRandom.Views.SettingsPages.General.SecuritySettingsPage;
using VoiceSettingsPage = SecRandom.Views.SettingsPages.Notification.VoiceSettingsPage;

namespace SecRandom;

public partial class App : Application
{
    private static FloatingWindow? _floatingWindow;
    private static Window? _quickDrawWindow;
    private static NotificationChannelSettings? _quickDrawNotificationSettings;
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private NativeMenuItem? _floatingWindowMenuItem;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private IHost? _mobileHost;
    private ISingleViewApplicationLifetime? _singleViewLifetime;
    private MobileViewHost? _mobileViewHost;
    private bool _mobileStopping;
    private bool _mobileStartupFailureShown;
    private readonly object _shutdownGate = new();
    private bool _isStopping;
    private bool _isOobeActive;
    public new static App Current => (Application.Current as App)!;
    internal bool IsStopping => _isStopping;
    public static bool IsDesktop;

    public TopLevel GetRootWindow()
    {
        if (_desktopLifetime?.Windows
                .Where(window => window.GetType().Name != "TrayPopupRoot"
                    && window is { IsActive: true, IsVisible: true, PlatformImpl: not null })
                .OrderBy(window => ReferenceEquals(window, _floatingWindow) ? 1 : 0)
                .FirstOrDefault() is TopLevel desktopRoot)
            return desktopRoot;

        if (_mobileViewHost is not null && TopLevel.GetTopLevel(_mobileViewHost) is { } mobileRoot)
            return mobileRoot;

        if (_floatingWindow is { PlatformImpl: not null } floatingRoot)
        {
            floatingRoot.Activate();
            return floatingRoot;
        }

        throw new InvalidOperationException("No active application TopLevel is available.");
    }

    public event EventHandler? AppStarted;
    public event EventHandler? AppStopping;

    public override void Initialize()
    {
        TouchInputModeAssist.Initialize();
        if (PlatformStartupContext.Current is MobilePlatformServiceRoot)
            Utils.ConfigureMobileDataRoot();

        // 初始化语言
        var mainConfig = new MainConfigModel();
        var settings = LoadStartupSettings(mainConfig);
        var culture = settings.Basic.Language switch
        {
            LanguageMode.ChineseSimplified => @"zh-Hans",
            LanguageMode.English => @"en-US",
            LanguageMode.Japanese => @"ja-JP",
            _ => @"zh-Hans"
        };
        InitializeLanguages(new CultureInfo(culture));
        if (PlatformStartupContext.Current is MobilePlatformServiceRoot)
            ApplyMobileCulture(new CultureInfo(culture));

        // 初始化 Avalonia App
        AvaloniaXamlLoader.Load(this);

        // 在 XAML 资源加载完成后立即应用外观设置（早于 BuildHost，确保重复实例对话框也能跟随主题）
        ApplyStartupAppearance(settings.Appearance);
        
        if (!Design.IsDesignMode && !OperatingSystem.IsMacOS() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
        {
            this.UseHotReload();
        }

#if DEBUG
        // 附加开发者工具
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var startupProtocolUri = ProtocolActivation.ConsumeStartupUri();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            WriteDesktopStartupDiagnostic("Desktop framework initialization started.");
            _desktopLifetime = desktop;
            IsDesktop = true;

            if (CrashRecoveryRuntime.StartupPromptOptions is { } promptOptions)
            {
                // 保持崩溃提示在单实例获取前，但建立 Host 以提供诊断导出和应用内反馈。
                try
                {
                    BuildHost(PlatformStartupContext.Current);
                }
                catch (Exception exception)
                {
                    WriteDesktopStartupDiagnostic("Crash-recovery Host build failed.", exception);
                }

                desktop.MainWindow = ShowCrashRecoveryPromptOnly(promptOptions);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // ===== 多实例检测（仅 Desktop Lifetime）=====
            if (!SingleInstanceService.Instance.TryAcquire())
            {
                // 已有实例在运行：创建临时宿主窗口显示对话框，跳过 BuildHost
                desktop.MainWindow = CreateDuplicateInstanceDialogHost(desktop, startupProtocolUri);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // 正常启动：注册 IPC 命令处理，再构建主机
            SingleInstanceService.Instance.CommandReceived += OnIpcCommandReceived;
            SingleInstanceService.Instance.RequestReceived += OnIpcRequestReceived;

            // 启动服务主机
            try
            {
                WriteDesktopStartupDiagnostic("Building desktop Host.");
                BuildHost(PlatformStartupContext.Current);
                WriteDesktopStartupDiagnostic("Desktop Host built.");
            }
            catch (Exception exception)
            {
                WriteDesktopStartupDiagnostic("Desktop Host build failed.", exception);
                throw;
            }

            if (IAppHost.GetService<FirstRunOobeService>().IsRequired())
            {
                ShowFirstRunOobe(desktop, startupProtocolUri);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            try
            {
                WriteDesktopStartupDiagnostic("Continuing desktop startup.");
                ContinueDesktopStartup(desktop, startupProtocolUri);
                WriteDesktopStartupDiagnostic("Desktop startup initialized.");
            }
            catch (Exception exception)
            {
                WriteDesktopStartupDiagnostic("Desktop startup initialization failed.", exception);
                throw;
            }
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            StartMobileApplication(singleView);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartMobileApplication(ISingleViewApplicationLifetime singleView)
    {
        _singleViewLifetime = singleView;
        Dispatcher.UIThread.UnhandledException += MobileDispatcherOnUnhandledException;
        try
        {
            BuildHost(PlatformStartupContext.Current);
            _mobileHost = IAppHost.Host;
            _mobileViewHost = ActivatorUtilities.CreateInstance<MobileViewHost>(_mobileHost!.Services);
            singleView.MainView = _mobileViewHost;
            ObserveTask(_mobileHost.Services.GetRequiredService<IViewEngine>().ShowAsync(GetMobileInitialViewId()),
                "Mobile root view activation failed.");

            if (singleView is IControlledApplicationLifetime controlled)
                controlled.Exit += (_, _) => _ = StopMobileHostAsync();

            ObserveTask(StartMobileHostAsync(_mobileHost), "Mobile runtime service startup failed.");
        }
        catch (Exception exception)
        {
            ReportMobileException(exception);
            if (ReferenceEquals(IAppHost.Host, _mobileHost))
                IAppHost.Host = null;
            _mobileHost?.Dispose();
            _mobileHost = null;
            ShowMobileStartupFailure(exception);
        }
    }

    private async Task StartMobileHostAsync(IHost host)
    {
        try
        {
            _ = host.Services.GetRequiredService<IProfileService>();
            _ = host.Services.GetRequiredService<IDrawTemporaryRecordService>();
            _ = host.Services.GetRequiredService<IFeatureAvailabilityService>();
            _ = host.Services.GetRequiredService<DrawEngine>();
            if (_mobileStopping || !ReferenceEquals(host, _mobileHost))
                return;

            await host.StartAsync().ConfigureAwait(false);
            if (_mobileStopping || !ReferenceEquals(host, _mobileHost))
                return;

            await host.Services.GetRequiredService<TelemetryRuntimeService>().InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportMobileException(exception);
            if (_mobileStopping || !ReferenceEquals(host, _mobileHost))
                return;

            await StopMobileHostAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => ShowMobileStartupFailure(exception));
        }
    }

    private async Task ReloadMobileRootViewAsync()
    {
        IHost? host = _mobileHost;
        ISingleViewApplicationLifetime? singleView = _singleViewLifetime;
        if (_mobileStopping || host is null || singleView is null)
            return;

        MobileViewHost? oldHost = _mobileViewHost;
        if (oldHost is not null)
        {
            var engine = host.Services.GetRequiredService<IViewEngine>();
            await engine.CloseHostAsync(oldHost, ViewCloseReason.Programmatic).ConfigureAwait(false);
            await oldHost.DestroyAsync().ConfigureAwait(false);
            await oldHost.DetachAsync().ConfigureAwait(false);
        }

        var newHost = ActivatorUtilities.CreateInstance<MobileViewHost>(host.Services);
        _mobileViewHost = newHost;
        await Dispatcher.UIThread.InvokeAsync(() => singleView.MainView = newHost);
        await host.Services.GetRequiredService<IViewEngine>().ShowAsync(GetMobileInitialViewId()).ConfigureAwait(false);
    }

    private static string GetMobileInitialViewId() =>
        (PlatformStartupContext.Current as MobilePlatformServiceRoot)?.UsesDesktopMainView == true
            ? DesktopViewIds.Main
            : MobilePageIds.Root;

    private async Task StopMobileHostAsync()
    {
        IHost? host = _mobileHost;
        if (_mobileStopping || host is null)
            return;

        _mobileStopping = true;
        try
        {
            if (_mobileViewHost is not null)
            {
                await host.Services.GetRequiredService<IViewEngine>()
                    .CloseHostAsync(_mobileViewHost, ViewCloseReason.ApplicationShutdown)
                    .ConfigureAwait(false);
                await _mobileViewHost.DestroyAsync().ConfigureAwait(false);
                await _mobileViewHost.DetachAsync().ConfigureAwait(false);
            }

            IMobileMediaPlayer mediaPlayer = host.Services.GetRequiredService<IMobileMediaPlayer>();
            await mediaPlayer.StopAsync().ConfigureAwait(false);
            if (mediaPlayer is IDisposable disposableMediaPlayer)
                disposableMediaPlayer.Dispose();
            await host.Services.GetRequiredService<TelemetryRuntimeService>().ShutdownAsync().ConfigureAwait(false);
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportMobileException(exception);
        }
        finally
        {
            if (ReferenceEquals(IAppHost.Host, host))
                IAppHost.Host = null;
            host.Dispose();
            if (ReferenceEquals(_mobileHost, host))
                _mobileHost = null;
            _mobileViewHost = null;
        }
    }

    private void MobileDispatcherOnUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TrySaveConfigForCrashRecovery();
        ReportMobileException(e.Exception);
        ObserveTask(CaptureUnhandledExceptionAsync(e.Exception),
            "Mobile unhandled exception telemetry capture failed.");
        e.Handled = true;

        CrashRecoveryPromptOptions? options = CrashRecoveryRuntime.TryCreateCurrentProcessPromptOptions(e.Exception);
        if (options is not null)
            ShowMobileCrashRecovery(options);
    }

    private void ShowMobileCrashRecovery(CrashRecoveryPromptOptions options)
    {
        CrashRecoveryViewState? state = IAppHost.TryGetService<CrashRecoveryViewState>();
        IViewEngine? engine = IAppHost.TryGetService<IViewEngine>();
        if (state is null || engine is null)
            return;

        state.Configure(options, static () => false, canIgnore: true);
        ObserveTask(engine.ShowAsync("system.crashRecovery"), "Mobile crash recovery display failed.");
    }

    private void ShowMobileStartupFailure(Exception exception)
    {
        if (_mobileStartupFailureShown || _singleViewLifetime is null)
            return;

        _mobileStartupFailureShown = true;
        _singleViewLifetime.MainView = CreateMobileStartupFailureView(exception);
    }

    internal static void ApplyMobileCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        MobileResources.Culture = culture;
    }

    private static Control CreateMobileStartupFailureView(Exception exception)
    {
        string startupFailedText;
        try
        {
            startupFailedText = MobileResources.M_StartupFailed;
        }
        catch (Exception resourceException)
        {
            startupFailedText = "SecRandom startup failed: " + resourceException.GetType().Name;
        }

        return new ScrollViewer
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

    private static void ReportMobileException(Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(exception);
        (PlatformStartupContext.Current as MobilePlatformServiceRoot)?.StartupErrorLogger?.Invoke(exception);
    }

    private void ContinueDesktopStartup(IClassicDesktopStyleApplicationLifetime desktop, string? startupProtocolUri)
    {
        _desktopLifetime = desktop;
        WriteDesktopStartupDiagnostic("Scheduling desktop runtime services.");
        ObserveTask(StartRuntimeServicesAsync(), "Runtime service startup failed.");
        WriteDesktopStartupDiagnostic("Creating floating window.");
        _floatingWindow = new FloatingWindow();
        _floatingWindow.Opened += (_, _) => RefreshTrayWindowMenuItems();
        _floatingWindow.Closed += (_, _) => _floatingWindow = null;
        if (!IAppHost.GetService<MainConfigHandler>().Data.FloatingWindowSettings.StartupDisplayFloatingWindow)
        {
            _floatingWindow.Hide();
            _floatingWindow.SetUserVisibilityIntent(false);
        }

        desktop.MainWindow = _floatingWindow;

        WriteDesktopStartupDiagnostic("Initializing desktop application chrome.");
        InitializeApp();
        WriteDesktopStartupDiagnostic("Desktop application chrome initialized.");
        if (startupProtocolUri is not null)
            Dispatcher.UIThread.Post(() => HandleProtocolUri(startupProtocolUri), DispatcherPriority.Render);

        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
        Dispatcher.UIThread.UnhandledException += App_OnDispatcherUnhandledException;
    }

    private void ShowFirstRunOobe(IClassicDesktopStyleApplicationLifetime desktop, string? startupProtocolUri)
    {
        _isOobeActive = true;
        var oobe = CreateFirstRunOobe(desktop, startupProtocolUri);
        desktop.MainWindow = oobe;
        oobe.Show();
    }

    private FirstRunOobeWindow CreateFirstRunOobe(
        IClassicDesktopStyleApplicationLifetime desktop,
        string? startupProtocolUri)
    {
        var oobe = new FirstRunOobeWindow();
        oobe.Completed += (_, _) =>
        {
            _isOobeActive = false;
            ContinueDesktopStartup(desktop, startupProtocolUri);
            ShowMainWindow();
        };
        oobe.LanguageChanged += (_, _) =>
        {
            var replacement = CreateFirstRunOobe(desktop, startupProtocolUri);
            desktop.MainWindow = replacement;
            replacement.Show();
            oobe.CloseForLanguageChange();
        };
        oobe.Closed += (_, _) =>
        {
            if (!oobe.IsCompleted && !oobe.IsReplacingForLanguageChange)
                RequestDesktopShutdown();
        };
        return oobe;
    }

    /// <summary>
    ///     创建多实例对话框宿主窗口。
    ///     宿主窗口本身不可见；对话框在 <see cref="Window.Opened"/> 异步事件中弹出，
    ///     避免在同步的 <see cref="OnFrameworkInitializationCompleted"/> 中阻塞 UI 线程。
    /// </summary>
    private static Window CreateDuplicateInstanceDialogHost(
        IClassicDesktopStyleApplicationLifetime _,
        string? startupProtocolUri = null)
    {
        var host = new Window
        {
            SizeToContent = SizeToContent.Manual,
            WindowState = WindowState.Maximized,
            ShowInTaskbar = false,
            CanResize = false,
            WindowDecorations = WindowDecorations.None,
            Background = null,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent }
        };

        // Opened 事件在 Dispatcher 事件循环内异步运行，不会死锁 UI 线程
        host.Opened += async (_, _) =>
        {
            if (startupProtocolUri is not null)
            {
                var sent = await SingleInstanceService.SendCommandAsync(SingleInstanceCommand.UrlPrefix +
                                                                        startupProtocolUri);
                if (sent)
                {
                    host.Close();
                    RequestDesktopShutdown();
                    return;
                }
            }

            var action = await DuplicateInstanceDialog.ShowAsync(host);

            switch (action)
            {
                case DuplicateInstanceAction.OpenExisting:
                    // 协议启动首发失败后仍优先重试原始命令，避免丢失 URL 激活。
                    await SingleInstanceService.SendCommandAsync(startupProtocolUri is null
                        ? SingleInstanceCommand.ShowMainWindow
                        : SingleInstanceCommand.UrlPrefix + startupProtocolUri);
                    break;

                case DuplicateInstanceAction.Restart:
                    // 通知第一个实例重启，稍作等待以确保对方有时间响应
                    await SingleInstanceService.SendCommandAsync(SingleInstanceCommand.Restart);
                    await Task.Delay(300);
                    break;

                case DuplicateInstanceAction.Cancel:
                default:
                    break;
            }

            // 所有分支最终都退出当前（重复）实例
            host.Close();
            RequestDesktopShutdown();
        };

        return host;
    }

    /// <summary>
    ///     处理来自后续实例的 IPC 命令（第一个实例专用）。
    ///     回调来自后台线程，需通过 <see cref="Dispatcher"/> 切换到 UI 线程。
    /// </summary>
    private void OnIpcCommandReceived(string command)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isOobeActive)
                return;

            switch (command)
            {
                case SingleInstanceCommand.ShowMainWindow:
                    ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
                        SecurityOperation.ToggleMainWindow,
                        () =>
                        {
                            ShowMainWindow();
                            return Task.CompletedTask;
                        }), "Legacy main-window authorization failed.");
                    break;
                case SingleInstanceCommand.Restart:
                    ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
                        SecurityOperation.RestartApplication,
                        () =>
                        {
                            Restart();
                            return Task.CompletedTask;
                        }), "Legacy restart authorization failed.");
                    break;
                default:
                    if (command.StartsWith(SingleInstanceCommand.UrlPrefix, StringComparison.Ordinal))
                        HandleProtocolUri(command[SingleInstanceCommand.UrlPrefix.Length..]);
                    break;
            }
        });
    }

    private Task<IpcResponseEnvelope> OnIpcRequestReceived(IpcRequestEnvelope request,
        CancellationToken cancellationToken)
    {
        if (_isOobeActive)
            return Task.FromResult(new IpcResponseEnvelope(true, "url",
                new IpcBusinessResult("error", "初始设置尚未完成。", "oobe_required")));

        return IAppHost.GetService<ProtocolCommandRouter>().HandleIpcAsync(request, cancellationToken);
    }

    private void HandleProtocolUri(string value)
    {
        ObserveTask(IAppHost.GetService<ProtocolCommandRouter>().HandleUrlAsync(value),
            "Protocol URL handling failed.");
    }

    private static Window ShowCrashRecoveryPromptOnly(CrashRecoveryPromptOptions promptOptions)
    {
        CrashRecoveryWindow window = new(promptOptions, RestartFromCrashRecoveryPrompt, canIgnore: false);
        window.Closed += (_, _) =>
        {
            if (!window.WasIgnored)
                RequestDesktopShutdown();
        };
        return window;
    }

    private static bool RestartFromCrashRecoveryPrompt()
    {
        return CrashRecoveryRuntime.TryRestartCurrentApp(
            CrashRecoveryRuntime.CreateRestartStartInfos([]),
            RequestDesktopShutdown);
    }

    private void BuildHost(IPlatformServiceRoot platform)
    {
        if (IAppHost.Host is not null) return;
        var mobilePlatform = platform as MobilePlatformServiceRoot;
        var isMobile = mobilePlatform is not null;
        var useMobileUI = isMobile && !mobilePlatform!.UsesDesktopMainView;

        IAppHost.Host = Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
                if (isMobile)
                {
                    services.AddPlatformServices(platform);
                    services.AddSingleton<SingleViewHostProvider>();
                    services.AddSingleton<IViewHostProvider>(provider =>
                        provider.GetRequiredService<SingleViewHostProvider>());
                    services.AddViewEngine()
                        .AddView<MobileRootView>(MobilePageIds.Root)
                        .AddView<MainView>(DesktopViewIds.Main)
                        .AddView<SettingsView>(MobilePageIds.Settings);
                    services.AddTransient<MobileViewHost>();
                }
                else
                {
                    services.AddPlatformServices(platform);
                    services.AddSingleton<DesktopViewHostProvider>();
                    services.AddSingleton<IViewHostProvider>(serviceProvider =>
                        serviceProvider.GetRequiredService<DesktopViewHostProvider>());
                    services.AddViewEngine()
                        .AddView<MainView>(DesktopViewIds.Main)
                        .AddView<SettingsView>(DesktopViewIds.Settings);
                }

                // 日志
                services.AddLogging(builder =>
                {
                    if (!isMobile)
                    {
                        builder.AddConsoleFormatter<LoggingConsoleFormatter, ConsoleFormatterOptions>();
                        builder.AddConsole(console => { console.FormatterName = @"secrandom"; });
                    }
                    
                    builder.AddSentry(options =>
                    {
                        // SDK 生命周期由 TelemetryRuntimeService 按隐私开关统一控制，日志 Provider 只复用已初始化的 SDK。
                        options.InitializeSdk = false;
                        options.MinimumEventLevel = LogLevel.Error;
                        // Sentry Structured Logs 默认关闭；日志 Provider 与 SDK 初始化选项都需要启用。
                        options.EnableLogs = true;
                    });
#if DEBUG
                    builder.SetMinimumLevel(LogLevel.Trace);
#endif
                });
                services.AddSingleton<ILoggerProvider, FileLoggerProvider>();

                // 配置
                services.AddCoreRuntimeServices();
                services.AddSingleton<DeviceUuidStore>();

                services.AddSingleton<ITelemetrySdkAdapter, SentryTelemetrySdkAdapter>();
                services.AddSingleton<TelemetryRuntimeService>();
                services.AddHostedService<OnlineStatusService>();

                // 服务
                if (isMobile)
                {
                    MobilePlatformServiceRoot currentMobilePlatform = mobilePlatform!;
                    services.AddTransient<MobileRollCallService>();
                    services.AddSingleton<MobileMediaLibraryService>();
                    services.AddSingleton<IMobileMediaPlayer>(currentMobilePlatform.MediaPlayer);
                    if (currentMobilePlatform.KeyboardOcclusionSource is { } keyboardOcclusionSource)
                        services.AddSingleton<IMobileKeyboardOcclusionSource>(keyboardOcclusionSource);
                    services.AddSingleton<MobileDrawMediaService>();
                    services.AddSingleton<IMobileUpdateInstaller>(currentMobilePlatform.UpdateInstaller);
                    services.AddHttpClient<MobileUpdateService>();
                }
                
                services.AddSingleton<IProfileQueryService, ProfileQueryService>();
                services.AddSingleton<DrawProofExportService>();
                services.AddSingleton<IVerificationKernel, ManagedVerificationKernel>();
                services.AddHttpClient<IWitnessClient, WitnessClient>(client =>
                    client.Timeout = TimeSpan.FromSeconds(3));
                services.AddSingleton<DrawProofAttestationService>();
                services.AddHostedService(serviceProvider =>
                    serviceProvider.GetRequiredService<DrawProofAttestationService>());
                services.AddTransient<VerificationDrawCoordinator>();
                services.AddSingleton<SettingsSearchService>();
                services.AddSingleton<FirstRunOobeService>();
                services.AddSingleton<OobeDataSetupService>();
                services.AddSingleton<IArchivePostImportHooks, DesktopArchivePostImportHooks>();
                services.AddSingleton<IImportExportService, ImportExportService>();
                services.AddSingleton<ISentryFeedbackClient, SentryFeedbackClient>();
                services.AddSingleton<IUserFeedbackService, UserFeedbackService>();
                services.AddHostedService<AutomaticBackupService>();
                services.AddHostedService<TaskBarIconService>();
                services.AddSingleton<GlobalShortcutService>();
                services.AddHostedService(serviceProvider =>
                    serviceProvider.GetRequiredService<GlobalShortcutService>());
                services.AddSingleton<DesktopIntegrationService>();
                services.AddSingleton<IExternalLauncher, ExternalLauncher>();
                services.AddHttpClient("updates", client => client.Timeout = TimeSpan.FromSeconds(30));
                services.AddSingleton<UpdateCenterService>(serviceProvider => new UpdateCenterService(
                    serviceProvider.GetRequiredService<MainConfigHandler>(),
                    serviceProvider.GetRequiredService<ILogger<UpdateCenterService>>(),
                    serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("updates")));
                services.AddSingleton<IUpdateNotificationService, UpdateNotificationService>();
                services.AddHostedService<UpdateScheduler>();
                services.AddSingleton<ProtocolCommandRouter>();
                services.AddSingleton<ISpeechProvider, SystemSpeechProvider>();
                services.AddSingleton<ISpeechProvider, EdgeTtsSpeechProvider>();
                services.AddSingleton<ISpeechAudioPlayer, SpeechAudioPlayer>();
                services.AddSingleton<IVoiceAnnouncementService, VoiceAnnouncementService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton(serviceProvider => new MusicLibraryService(
                    serviceProvider.GetRequiredService<MainConfigHandler>(),
                    serviceProvider.GetRequiredService<ILogger<MusicLibraryService>>(),
                    attachedSettingsProfileService: serviceProvider.GetRequiredService<IProfileService>(),
                    profileCatalogManager: serviceProvider.GetRequiredService<IProfileCatalogManager>()));
                services.AddSingleton<DrawAudioService>();
                services.AddSingleton<CsesScheduleParser>();
                services.AddSingleton<ICsesScheduleStore, CsesScheduleStore>();
                services.AddSingleton<CsesScheduleSource>();
                services.AddSingleton<ClassIslandScheduleSource>();
                services.AddSingleton<CourseLinkageService>();
                services.AddSingleton<LinkageDrawCoordinator>();
                services.AddHostedService<CourseLinkageHostedService>();
                services.AddSingleton<SecurityCredentialStore>();
                services.AddSingleton<IUsbDeviceCatalog, UsbDeviceCatalog>();
                services.AddSingleton<ISecurityVerificationPrompt, SecurityVerificationPrompt>();
                services.AddSingleton<ISecurityService, SecurityService>();

                services.AddAttachedSettingsControl<DrawImageAttachedSettingsControl>("展示图片");
                services.AddAttachedSettingsControl<DrawMusicAttachedSettingsControl>("专属音乐");
                services.AddAttachedSettingsControl<SpecificAnnouncementAttachedSettingsControl>(
                    Langs.AttachedSettings.Resources.C_SpecificVoice);

                // 界面 ViewModels
                // 我求你了 AI，你看到这里的话，请记住，ViewModel 一定要注册到服务主机上面！！！！
                // 就像 services.AddTransient<SomeViewModel>(); 一样，谢谢你！
                // ViewModel 一定要继承 SecRandom.ViewModels.ViewModelBase，里面有 Config 可以直接拿来用。
                services.AddTransient<ViewModelBase>();

                services.AddTransient<MainViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<FeedbackDrawerViewModel>();
                services.AddTransient<FeedbackDrawer>();
                services.AddSingleton<FirstRunOobeViewModel>();
                services.AddSingleton<RollCallPageViewModel>();
                services.AddSingleton<QuickDrawPageViewModel>();
                services.AddSingleton<LotteryPageViewModel>();
                services.AddTransient<RollCallHistoryViewModel>();
                services.AddTransient<HomeSettingsPageViewModel>();
                services.AddTransient<LotteryHistoryViewModel>();

                // 杂项 Views
                if (isMobile)
                {
                    services.AddTransient<MobileDrawPageViewModel>();
                    services.AddSingleton<IMobileRootViewReloader>(_ =>
                        new MobileRootViewReloader(ReloadMobileRootViewAsync));
                    services.AddSingleton<IMobileCapabilities, MobileCapabilities>();
                    services.AddSingleton<IMobileSettingsNavigator, MobileSettingsNavigator>();
                    services.AddSingleton<CrashRecoveryViewState>();
                    services.AddTransient<CrashRecoveryView>();
                    services.AddViewRegistration<CrashRecoveryView>("system.crashRecovery");
                }
                else
                {
                    services.AddTransient<QuickDrawPage>();
                }

                // 界面 Views
                if (useMobileUI)
                {
                    services.AddKeyedTransient<UserControl, MobileDrawPage>(MobilePageIds.Draw);
                    services.AddKeyedTransient<UserControl, MobileHistoryPage>(MobilePageIds.History);
                    services.AddKeyedTransient<UserControl, MobileOverviewPage>(MobilePageIds.Overview);
                }
                else
                {
                    services.AddMainPage<RollCallPage>(Langs.Common.Resources.Feat_RollCall);
                    services.AddMainPage<LotteryPage>(Langs.Common.Resources.Feat_Lottery);
                    services.AddMainPage<HistoryPage>(Langs.Common.Resources.Feat_History);
                }

                // 设置界面 Views
                services.AddSettingsPage<LogViewerSettingsPage>(Langs.SettingsPages.LogViewer.Resources.Page_Title);
                
                if (isMobile && useMobileUI)
                {
                    services.AddSettingsPage<MobileSettingsCatalogPage>(MobileResources.P_Settings);
                    
                    // 顶部和偏好
                    services.AddGroup(new PageGroupInfo(
                        MobileResources.S_GroupPreferences, "settings.mobile.preferences", FluentIcons.ColorFilled));
                    services.AddSettingsPage<MobileGeneralSettingsPage>(MobileResources.S_General);
                    services.AddSettingsPage<MobilePersonalizationSettingsPage>(MobileResources.S_Personalization);
                    services.AddSettingsPage<MobileDrawSettingsPage>(MobileResources.S_DrawSettings);
                
                    // 数据
                    services.AddGroup(new PageGroupInfo(
                        MobileResources.S_GroupData, "settings.mobile.data", FluentIcons.DatabaseFilled));
                    services.AddSettingsPage<MobileListManagementSettingsPage>(MobileResources.S_ListManagement);
                    services.AddSettingsPage<MobileBackupSettingsPage>(MobileResources.S_Backup);
                
                    // 应用
                    services.AddGroup(new PageGroupInfo(
                        MobileResources.S_GroupApp, "settings.mobile.application", FluentIcons.InfoFilled));
                    if (mobilePlatform!.UpdateInstaller.IsSupported)
                        services.AddSettingsPage<MobileUpdateSettingsPage>(MobileResources.S_AppUpdates);
                    services.AddSettingsPage<MobileAboutSettingsPage>(MobileResources.S_About);
                }
                else
                {
                    // services.AddSettingsPage<MobileSettingsCatalogPage>(MobileResources.P_Settings);
                    
                    // 顶部
                    services.AddSettingsPage<HomeSettingsPage>(Langs.Common.Resources.Settings_Home);
                    services.AddSettingsPageSeparator();

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Settings_General, "settings.general", FluentIcons.SettingsFilled));
                    services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.Settings_Basic);
                    services.AddSettingsPage<SecuritySettingsPage>(Langs.Common.Resources.Settings_Security);
                    services.AddSettingsPage<PrivacySettingsPage>(Langs.SettingsPages.General.Privacy.Resources
                        .Page_Title);
                    services.AddSettingsPage<VerificationSettingsPage>(Langs.SettingsPages.General.Verification
                        .Resources.Page_Title);
                    services.AddSettingsPage<BackupSettingsPage>(Langs.Common.Resources.Settings_Backup);

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Settings_Personalized, "settings.personalized",
                        FluentIcons.ColorFilled));
                    services.AddSettingsPage<AppearanceSettingsPage>(Langs.Common.Resources.Settings_Appearance);
                    services.AddSettingsPage<FloatingWindowSettingsPage>(Langs.Common.Resources
                        .Settings_FloatingWindow);
                    services.AddSettingsPage<MusicSettingsPage>(Langs.SettingsPages.Personalized.Music.Resources
                        .Page_Title);
                    services.AddSettingsPage<LinkageSettingsPage>(Langs.Common.Resources.Settings_Linkage);
                    services.AddSettingsPage<MoreSettingsPage>(Langs.SettingsPages.More.Resources.Page_Title);

                    services.AddSettingsPageSeparator();

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Settings_RosterManagement, "settings.listManagement",
                        FluentIcons.PeopleListFilled));
                    services.AddSettingsPage<RollCallListSettingsPage>(Langs.SettingsPages.ListManagement.RollCallList
                        .Resources.Page_Title);
                    services.AddSettingsPage<LotteryListSettingsPage>(Langs.SettingsPages.ListManagement.LotteryList
                        .Resources.Page_Title);

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Settings_Draw, "settings.picking", FluentIcons.SettingsFilled));
                    services.AddSettingsPage<DefaultDrawSettingsPage>(
                        Langs.SettingsPages.Picking.Resources.Page_Default);
                    services.AddSettingsPage<RollCallDrawSettingsPage>(Langs.SettingsPages.Picking.Resources
                        .Page_RollCall);
                    services.AddSettingsPage<QuickDrawSettingsPage>(
                        Langs.SettingsPages.Picking.Resources.Page_QuickDraw);
                    services.AddSettingsPage<LotteryDrawSettingsPage>(
                        Langs.SettingsPages.Picking.Resources.Page_Lottery);

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Settings_Notification, "settings.notification",
                        FluentIcons.CommentNoteFilled));
                    services.AddSettingsPage<VoiceSettingsPage>(Langs.Common.Resources.Settings_Voice);
                    services.AddSettingsPage<DefaultNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources
                        .Page_Title);
                    services.AddSettingsPage<RollCallNotificationSettingsPage>(Langs.Common.Resources
                        .Settings_RollCallNotification);
                    services.AddSettingsPage<QuickDrawNotificationSettingsPage>(Langs.Common.Resources
                        .Settings_QuickDrawNotification);
                    services.AddSettingsPage<LotteryNotificationSettingsPage>(Langs.Common.Resources
                        .Settings_LotteryNotification);

                    services.AddGroup(new PageGroupInfo(
                        Langs.Common.Resources.Feat_History, "settings.history", FluentIcons.HistoryFilled));
                    services.AddSettingsPage<HistoryManagementSettingsPage>(Langs.Common.Resources
                        .Settings_HistoryManagement);
                    services.AddSettingsPage<RollCallHistorySettingsPage>(Langs.Common.Resources.Feat_RollCallHistory);
                    services.AddSettingsPage<LotteryHistorySettingsPage>(Langs.Common.Resources.Feat_LotteryHistory);

                    services.AddSettingsPageSeparator(isHide: true);
                    // services.AddSettingsPage<PluginsSettingsPage>(Langs.SettingsPages.Plugins.Overview.Resources
                    //     .Page_Title);

                    // 底部
                    services.AddSettingsPage<UpdateSettingsPage>(Langs.Common.Resources.Settings_Update);
                    services.AddSettingsPage<AboutSettingsPage>(Langs.Common.Resources.Settings_About);

                    services.AddSettingsPageSeparator(PageLocation.Bottom, isHide: true);
                    services.AddSettingsPage<DebugSettingsPage>(
                        Langs.SettingsPages.Debug.DebugStrings.Get("Page_Title"));
                }

            })
            .Build();

        var logger = IAppHost.GetService<ILogger<App>>();

        logger.LogInformation(@"SecRandom {VERSION} (Codename: {CODENAME})", GlobalConstants.Version,
            GlobalConstants.CodeName);
        logger.LogInformation(@"Copyright by SECTL(2025~{YEAR})  Licensed under GPL3.0", DateTime.Now.Year);
        logger.LogInformation("Host built.");

        // 刷新个性化设置
        RefreshPersonalizedSettings();

        IAppHost.GetService<IProfileService>();

        // RESOURCES TEST
        var isVisible = false;
        if (GlobalConstants.IsDevelopment && isVisible)
            IAppHost.GetService<SettingsSearchService>().LogTestInformation();
    }

    private static MainConfigModel LoadStartupSettings(MainConfigModel fallback)
    {
        if (!File.Exists(fallback.ConfigFilePath))
            return fallback;

        try
        {
            return JsonSerializer.Deserialize<MainConfigModel>(
                File.ReadAllText(fallback.ConfigFilePath),
                ConfigServiceBase.JsonOptions) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public void InitializeApp()
    {
        var taskBarIconService = IAppHost.Host!.Services
            .GetServices<IHostedService>().OfType<TaskBarIconService>().First();
        var menu = this.FindResource(@"AppMenu") as NativeMenu;
        taskBarIconService.MainTaskBarIcon.Menu = menu;
        _floatingWindowMenuItem = menu?.Items.ElementAtOrDefault(3) as NativeMenuItem;
        RefreshTrayWindowMenuItems();
        taskBarIconService.MainTaskBarIcon.IsVisible = true;
        taskBarIconService.MainTaskBarIcon.Clicked += MainTaskBarIconOnClicked;
        IAppHost.GetService<DesktopIntegrationService>().EnsureConfiguredIntegrations();

        if (IAppHost.GetService<MainConfigHandler>().Data.General.Basic.ShowStartupWindow)
            Dispatcher.UIThread.Post(ShowMainWindow, DispatcherPriority.Render);

        AppStarted?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        ObserveTask(StopAsync(), "Application shutdown failed.");
    }

    public async Task StopAsync()
    {
        await StopAsync(requestLifetimeShutdown: true).ConfigureAwait(false);
    }

    private async Task StopAsync(bool requestLifetimeShutdown)
    {
        lock (_shutdownGate)
        {
            if (_isStopping)
            {
                IAppHost.TryGetService<ILogger<App>>()?
                    .LogDebug("Skipping duplicate application shutdown request.");
                return;
            }

            _isStopping = true;
        }

        IAppHost.TryGetService<ILogger<App>>()?
            .LogInformation("Stopping application.");

        AppStopping?.Invoke(this, EventArgs.Empty);

        _floatingWindow?.CanClose = true;

        IAppHost.GetService<MainConfigHandler>().Save();
        IAppHost.GetService<IProfileService>().SaveProfile();
        await ShutdownTelemetryAsync().ConfigureAwait(false);

        IHost? host = IAppHost.Host;
        if (host is not null)
        {
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            host.Dispose();
        }

        IAppHost.Host = null;

        // 释放单实例 Mutex 及 IPC 管道
        SingleInstanceService.Instance.Dispose();

        if (requestLifetimeShutdown)
            RequestDesktopShutdown();
    }

    public async void Restart()
    {
        var startInfos = CrashRecoveryRuntime.CreateRestartStartInfos([]);

        TrySaveConfigForCrashRecovery();
        SingleInstanceService.Instance.Dispose();

        if (!CrashRecoveryRuntime.TryRestartCurrentApp(startInfos, static () => { }))
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError("Application restart launch phase failed.");
            return;
        }

        try
        {
            await StopAsync(requestLifetimeShutdown: false).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Application restart shutdown phase failed.");
            return;
        }

        RequestDesktopShutdown();
    }

    public async Task RestartThroughLauncherAsync()
    {
        var launcherName = OperatingSystem.IsWindows() ? "SecRandomLauncher.exe" : "SecRandomLauncher";
        var launcherPath = Path.Combine(Utils.PackageRoot, launcherName);
        if (!File.Exists(launcherPath))
            throw new FileNotFoundException("便携版 Launcher 不存在。", launcherPath);

        await StopAsync(requestLifetimeShutdown: false).ConfigureAwait(false);
        var startInfo = new ProcessStartInfo(launcherPath)
        {
            WorkingDirectory = Utils.PackageRoot,
            UseShellExecute = false
        };
        Process.Start(startInfo);
        RequestDesktopShutdown();
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TrySaveConfigForCrashRecovery();
        TryLogCritical(e.Exception);
        ObserveTask(CaptureUnhandledExceptionAsync(e.Exception), "Unhandled exception telemetry capture failed.");

        e.Handled = DispatcherCrashRecovery.TryRecover(
            e.Exception,
            CrashRecoveryRuntime.TryCreateCurrentProcessPromptOptions,
            ShowCrashRecoveryPrompt,
            CrashRecoveryRuntime.TryHandlePromptDisplayFailure,
            CrashRecoveryRuntime.TryHandleFatalException,
            RequestDesktopShutdown);
    }

    private static bool ShowCrashRecoveryPrompt(CrashRecoveryPromptOptions promptOptions)
    {
        CrashRecoveryWindow window = new(promptOptions, RestartFromCrashRecoveryCurrentApp);
        window.Closed += (_, _) =>
        {
            if (!window.WasIgnored)
                RequestDesktopShutdown();
        };
        window.Show();
        return true;
    }

    private static bool RestartFromCrashRecoveryCurrentApp()
    {
        Current.Restart();
        return true;
    }

    private static void TrySaveConfigForCrashRecovery()
    {
        try
        {
            IAppHost.TryGetService<MainConfigHandler>()?.Save();
            IAppHost.TryGetService<IProfileService>()?.SaveProfile();
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Crash-recovery persistence failed.");
        }
    }

    private static void TryLogCritical(Exception exception)
    {
        try
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogCritical(exception, "Unhandled application exception.");
        }
        catch
        {
        }
    }

    private void CurrentDomainOnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            IAppHost.TryGetService<MainConfigHandler>()?.Save();
            IAppHost.TryGetService<IProfileService>()?.SaveProfile();
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Process-exit persistence failed.");
        }
    }

    /// <summary>
    /// 初始化遥测运行时服务，由 <see cref="StartRuntimeServicesAsync"/> 在 Host 启动前调用。
    /// </summary>
    private static async Task InitializeRuntimeServicesAsync()
    {
        try
        {
            var telemetry = IAppHost.GetService<TelemetryRuntimeService>();
            await telemetry.InitializeAsync().ConfigureAwait(false);
            // await SendSentryTestEventAsync(telemetry).ConfigureAwait(false);
            // Dispatcher.UIThread.Post(() => throw new InvalidOperationException("SENTRY_TEST_FAKE_ERROR_UNHANDLED"));
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Telemetry initialization failed.");
        }
    }

    /// <summary>
    /// 按顺序启动遥测和 Host，确保 SDK 在 HostedService 启动前就绪。
    /// </summary>
    private static async Task StartRuntimeServicesAsync()
    {
        await InitializeRuntimeServicesAsync().ConfigureAwait(false);
        await IAppHost.Host!.StartAsync().ConfigureAwait(false);
    }

    private static void ObserveTask(Task task, string failureMessage)
    {
        _ = ObserveTaskAsync(task, failureMessage);
    }

    private static async Task ObserveTaskAsync(Task task, string failureMessage)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            WriteDesktopStartupDiagnostic(failureMessage, ex);
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, failureMessage);
        }
    }

    private static void WriteDesktopStartupDiagnostic(string message, Exception? exception = null)
    {
        try
        {
            string directory = Utils.GetFilePath("logs");
            Directory.CreateDirectory(directory);
            string entry = $"{DateTimeOffset.Now:O} [desktop-startup] {message}{Environment.NewLine}";
            if (exception is not null)
                entry += exception + Environment.NewLine;
            File.AppendAllText(Path.Combine(directory, "desktop-startup.log"), entry);
        }
        catch
        {
            System.Diagnostics.Debug.WriteLine($"[desktop-startup] {message}\n{exception}");
        }
    }

    private static async Task CaptureUnhandledExceptionAsync(Exception exception)
    {
        try
        {
            TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
            if (telemetry is not null)
                await telemetry.CaptureExceptionAsync(exception).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Unhandled exception telemetry capture failed.");
        }
    }

    private static async Task SendSentryTestEventAsync(TelemetryRuntimeService telemetry)
    {
        var exception = new InvalidOperationException("SENTRY_TEST_FAKE_ERROR");
        var logger = IAppHost.GetService<ILogger<App>>();

        logger.LogError(exception, "SENTRY_TEST_FAKE_ERROR log event.");
        await telemetry.CaptureExceptionAsync(exception).ConfigureAwait(false);
        await telemetry.FlushAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
    }

    private static async Task ShutdownTelemetryAsync()
    {
        try
        {
            TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
            if (telemetry is not null)
                await telemetry.ShutdownAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, "Telemetry shutdown failed.");
        }
    }

    private static void RequestDesktopShutdown()
    {
        if (_desktopLifetime is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            _desktopLifetime.Shutdown();
            return;
        }

        Dispatcher.UIThread.Post(() => _desktopLifetime?.Shutdown());
    }

    public static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.CurrentCulture = cultureInfo;
        CultureInfo.CurrentUICulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
        Langs.FirstRunOobe.Resources.Culture = cultureInfo;
        Langs.MainPages.QuickDraw.Resources.Culture = cultureInfo;
    }

    /// <summary>
    ///     在 XAML 资源加载完成后立即应用外观设置（主题色、主题模式），
    ///     无需 DI，可在 BuildHost 之前调用，确保重复实例对话框也能跟随用户主题。
    /// </summary>
    private void ApplyStartupAppearance(AppearanceSettingsConfig settings)
    {
        RequestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Auto => ThemeVariant.Default,
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        var fluentAvaloniaTheme = this.FindResource(@"FluentAvaloniaTheme") as FluentAvaloniaTheme;
        if (fluentAvaloniaTheme is null) return;

        fluentAvaloniaTheme.PreferSystemTheme = settings.Theme == ThemeMode.Auto;

        if (settings.ThemeColorMode == ThemeColorMode.System)
        {
            fluentAvaloniaTheme.PreferUserAccentColor = true;
            fluentAvaloniaTheme.CustomAccentColor = null;
        }
        else
        {
            fluentAvaloniaTheme.PreferUserAccentColor = false;
            fluentAvaloniaTheme.CustomAccentColor = settings.ThemeColor;
        }

        Resources[@"NavigationViewItemOnLeftIconBoxHeight"] = 20.0;
    }

    public void RefreshPersonalizedSettings()
    {
        var config = IAppHost.GetService<MainConfigHandler>().Data;
        var settings = config.Appearance;

        var fontFamily = settings.Font;
        if (fontFamily == @"MiSans")
            fontFamily = @"avares://SecRandom/Assets/Fonts/MiSans/#MiSans";

        // 主题模式
        RequestedThemeVariant = settings.Theme switch
        {
            ThemeMode.Auto => ThemeVariant.Default,
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        var fluentAvaloniaTheme = this.FindResource(@"FluentAvaloniaTheme") as FluentAvaloniaTheme;
        fluentAvaloniaTheme?.PreferSystemTheme = settings.Theme == ThemeMode.Auto;

        // 主题色
        if (settings.ThemeColorMode == ThemeColorMode.System)
        {
            fluentAvaloniaTheme?.PreferUserAccentColor = true;
            fluentAvaloniaTheme?.CustomAccentColor = null;
        }
        else
        {
            fluentAvaloniaTheme?.PreferUserAccentColor = false;
            fluentAvaloniaTheme?.CustomAccentColor = settings.ThemeColor;
        }

        Resources[@"ContentControlThemeFontFamily"] = Resources[@"AppFontFamily"] = new FontFamily(fontFamily);
        Resources[@"AppFontWeight"] = Enum.Parse<FontWeight>(settings.FontWeight.ToString());
    }

    #region Windows

    public static void ShowMainWindow() => ShowMainWindow(null);

    public static void ShowMainWindow(string? pageId)
    {
        ObserveTask(ShowMainWindowCoreAsync(pageId), "Failed to show main window.");
    }

    private static async Task ShowMainWindowCoreAsync(string? pageId = null)
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.main_window", "ui.navigation");

        try
        {
            WriteDesktopStartupDiagnostic("Showing main window.");
            if (_mainWindow is null)
            {
                WriteDesktopStartupDiagnostic("Creating main window and view host.");
                var mainWindow = _mainWindow = new MainWindow(MainWindowSettingsScope.Primary)
                {
                    Title = @"SecRandom"
                };
                var host = new DesktopWindowViewHost(mainWindow, DesktopViewIds.Main);
                IAppHost.GetService<DesktopViewHostProvider>().RegisterHost(host);
                mainWindow.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_mainWindow, mainWindow))
                        _mainWindow = null;
                };
            }

            await IAppHost.GetService<IViewEngine>().ShowAsync(
                DesktopViewIds.Main,
                new ViewShowOptions { HostId = DesktopViewIds.Main }).ConfigureAwait(true);
            WriteDesktopStartupDiagnostic("Main window view displayed.");
            if (!string.IsNullOrWhiteSpace(pageId))
                MainView.Current?.SelectNavigationItemById(pageId);
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            WriteDesktopStartupDiagnostic("Main window display failed.", ex);
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show main window.");
            throw;
        }
    }

    public static void ToggleMainWindow(string? pageId = null)
    {
        if (pageId == "main.lottery" && !IAppHost.GetService<IFeatureAvailabilityService>().IsLotteryEnabled)
            return;

        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.ToggleMainWindow,
            () => ShowMainWindowCoreAsync(pageId)), "Main window authorization failed.");
    }

    public static void SetMainWindowVisibility(string action, string? pageId = null)
    {
        ObserveTask(SetMainWindowVisibilityCoreAsync(action, pageId), "Main window visibility update failed.");
    }

    private static async Task SetMainWindowVisibilityCoreAsync(string action, string? pageId)
    {
        if (pageId == "main.lottery" && !IAppHost.GetService<IFeatureAvailabilityService>().IsLotteryEnabled)
            return;

        var shouldShow = action switch
        {
            "show" => true,
            "hide" => false,
            _ => _mainWindow is not { IsVisible: true }
        };
        if (shouldShow)
        {
            await ShowMainWindowCoreAsync(pageId);
        }
        else
        {
            _mainWindow?.Hide();
        }
    }

    internal static void RequestExitFromMainWindow()
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.ExitApplication,
            () =>
            {
                Current.Stop();
                return Task.CompletedTask;
            }), "Main window exit authorization failed.");
    }

    public static void ToggleFloatingWindow()
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.ToggleFloatingWindow,
            () =>
            {
                if (_floatingWindow is { IsVisible: true })
                {
                    _floatingWindow.Hide();
                    _floatingWindow.SetUserVisibilityIntent(false);
                }
                else if (_floatingWindow is not null)
                {
                    _floatingWindow.SetUserVisibilityIntent(true);
                    if (!_floatingWindow.IsHiddenByCourseLinkage)
                        RestoreWithoutActivating(_floatingWindow);
                }

                Current.RefreshTrayWindowMenuItems();
                return Task.CompletedTask;
            }), "Floating window authorization failed.");
    }

    public static void SetFloatingWindowVisibility(string action)
    {
        var shouldShow = action switch
        {
            "show" => true,
            "hide" => false,
            _ => _floatingWindow is not { UserWantsVisible: true }
        };
        if (shouldShow && _floatingWindow is not null)
        {
            _floatingWindow.SetUserVisibilityIntent(true);
            if (!_floatingWindow.IsHiddenByCourseLinkage)
                RestoreWithoutActivating(_floatingWindow);
        }
        else if (!shouldShow)
        {
            _floatingWindow?.Hide();
            _floatingWindow?.SetUserVisibilityIntent(false);
        }

        Current.RefreshTrayWindowMenuItems();
    }

    public static void ShowSettingsWindow() => ShowSettingsWindow(null);

    public static void ShowSettingsWindow(string? pageId)
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeSettingsAsync(
            async () =>
            {
                await ShowSettingsWindowCoreAsync();
                SettingsView.Current?.ExitPreview();
                if (!string.IsNullOrWhiteSpace(pageId))
                    SettingsView.Current?.NavigateToPage(pageId);
            },
            async () =>
            {
                await ShowSettingsWindowCoreAsync();
                SettingsView.Current?.NavigateToPreviewPage(pageId ?? "settings.general.basic");
            }), "Settings window authorization failed.");
    }

    public static void SetSettingsWindowVisibility(string action, string pageId, bool preview)
    {
        ObserveTask(SetSettingsWindowVisibilityCoreAsync(action, pageId, preview),
            "Settings window visibility update failed.");
    }

    private static async Task SetSettingsWindowVisibilityCoreAsync(string action, string pageId, bool preview)
    {
        var shouldShow = action switch
        {
            "show" => true,
            "hide" => false,
            _ => _settingsWindow is not { IsVisible: true }
        };
        if (shouldShow)
        {
            await ShowSettingsWindowCoreAsync();
            if (preview)
                SettingsView.Current?.NavigateToPreviewPage(pageId);
            else
                SettingsView.Current?.NavigateToPage(pageId);
        }
        else
        {
            _settingsWindow?.Hide();
        }
    }

    private static async Task ShowSettingsWindowCoreAsync()
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.settings_window", "ui.navigation");

        try
        {
            if (_settingsWindow is null)
            {
                var settingsWindow = _settingsWindow = new MainWindow(MainWindowSettingsScope.Settings)
                {
                    Title = @"SecRandom"
                };
                var host = new DesktopWindowViewHost(settingsWindow, DesktopViewIds.Settings);
                IAppHost.GetService<DesktopViewHostProvider>().RegisterHost(host);
                settingsWindow.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_settingsWindow, settingsWindow))
                        _settingsWindow = null;
                };
            }

            await IAppHost.GetService<IViewEngine>().ShowAsync(
                DesktopViewIds.Settings,
                new ViewShowOptions { HostId = DesktopViewIds.Settings }).ConfigureAwait(true);
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show settings window.");
            throw;
        }
    }

    public static void ShowQuickDrawWindow()
    {
        ObserveTask(ShowQuickDrawWindowAsync(), "Quick draw window action failed.");
    }

    public static void ShowQuickDrawNotificationWindow(
        IReadOnlyCollection<string> items,
        int autoCloseTime,
        bool animate,
        bool preserveQuickDrawResult,
        NotificationChannelSettings windowSettings,
        bool showPreview)
    {
        Dispatcher.UIThread.Post(() => ObserveTask(
            ShowQuickDrawNotificationWindowAsync(items, autoCloseTime, animate, preserveQuickDrawResult, windowSettings,
                showPreview),
            "Quick draw notification window action failed."));
    }

    public static Task ShowQuickDrawNotificationPreviewWindowAsync(NotificationChannelSettings windowSettings)
    {
        return Dispatcher.UIThread.InvokeAsync(() =>
        {
            _quickDrawNotificationSettings = windowSettings;
            var quickDraw = IAppHost.GetService<QuickDrawPageViewModel>();
            quickDraw.NotificationOpacity = Math.Clamp(windowSettings.Transparency, 20, 100);
            var window = GetOrCreateQuickDrawWindow();
            window.Opacity = 1;
            if (!window.IsVisible)
                window.Show();
            window.Activate();
            Dispatcher.UIThread.Post(
                () => PositionQuickDrawNotificationWindow(window, windowSettings),
                DispatcherPriority.Render);
        }).GetTask();
    }

    public static void HideQuickDrawNotificationWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_quickDrawWindow is { IsVisible: true })
                _quickDrawWindow.Hide();
        });
    }

    private static async Task ShowQuickDrawNotificationWindowAsync(
        IReadOnlyCollection<string> items,
        int autoCloseTime,
        bool animate,
        bool preserveQuickDrawResult,
        NotificationChannelSettings windowSettings,
        bool showPreview)
    {
        _quickDrawNotificationSettings = windowSettings;
        var quickDraw = IAppHost.GetService<QuickDrawPageViewModel>();
        quickDraw.NotificationOpacity = Math.Clamp(windowSettings.Transparency, 20, 100);
        var window = GetOrCreateQuickDrawWindow();
        window.Opacity = 1;
        if (!window.IsVisible)
            window.Show();
        window.Activate();
        if (showPreview)
        {
            quickDraw.ShowNotificationPreview(items, preserveQuickDrawResult);
            await Task.Delay(quickDraw.PreviewAnimationDuration);
        }

        quickDraw.ShowNotificationResult(items, autoCloseTime, animate, preserveQuickDrawResult);
        Dispatcher.UIThread.Post(
            () => PositionQuickDrawNotificationWindow(window, windowSettings),
            DispatcherPriority.Render);
    }

    private static async Task ShowQuickDrawWindowAsync()
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.quick_draw_window", "ui.navigation");

        try
        {
            _quickDrawNotificationSettings = null;
            var quickDraw = IAppHost.GetService<QuickDrawPageViewModel>();
            quickDraw.ClearNotificationPresentation();
            if (!quickDraw.IsDrawing && !await quickDraw.AuthorizeTriggeredDrawAsync())
            {
                transaction?.Finish(SpanStatus.PermissionDenied);
                return;
            }

            var showBuiltInNotificationAnimation = IAppHost.GetService<NotificationService>()
                .UsesBuiltInNotificationService(NotificationSettingsType.QuickDraw);
            if (!showBuiltInNotificationAnimation)
            {
                HideQuickDrawNotificationWindow();
                if (!quickDraw.IsDrawing)
                    await quickDraw.StartAuthorizedTriggeredDrawAsync();
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (!quickDraw.IsDrawing)
                await quickDraw.StartAuthorizedTriggeredDrawAsync();
            else if (_quickDrawWindow is { IsVisible: true })
                _quickDrawWindow.Activate();
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show quick draw window.");
            throw;
        }
    }

    private static Window GetOrCreateQuickDrawWindow()
    {
        if (_quickDrawWindow is { IsLoaded: true })
            return _quickDrawWindow;

        _quickDrawWindow = new Window
        {
            Content = IAppHost.GetService<QuickDrawPage>(),
            Title = @"SecRandom",
            MinWidth = 280,
            MinHeight = 160,
            SizeToContent = SizeToContent.WidthAndHeight,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            WindowDecorations = WindowDecorations.None,
            CanMinimize = false,
            CanMaximize = false,
            CanResize = false,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent]
        };
        _quickDrawWindow.Opened += (_, _) =>
        {
            ApplyQuickDrawWindowBounds(_quickDrawWindow);
            Dispatcher.UIThread.Post(
                () => PositionOrCenterQuickDrawWindow(_quickDrawWindow),
                DispatcherPriority.Render);
        };
        _quickDrawWindow.SizeChanged += (_, _) => Dispatcher.UIThread.Post(
            () => PositionOrCenterQuickDrawWindow(_quickDrawWindow),
            DispatcherPriority.Render);
        _quickDrawWindow.PositionChanged += (_, _) => ApplyQuickDrawWindowBounds(_quickDrawWindow);
        _quickDrawWindow.ScalingChanged += (_, _) => ApplyQuickDrawWindowBounds(_quickDrawWindow);
        _quickDrawWindow.Closed += (_, _) =>
        {
            _quickDrawWindow = null;
            _quickDrawNotificationSettings = null;
        };
        return _quickDrawWindow;
    }

    private static void PositionOrCenterQuickDrawWindow(Window? window)
    {
        if (_quickDrawNotificationSettings is { } settings)
            PositionQuickDrawNotificationWindow(window, settings);
        else
            CenterQuickDrawWindow(window);
    }

    private static void PositionQuickDrawNotificationWindow(
        Window? window,
        NotificationChannelSettings settings)
    {
        if (window is null)
            return;

        var nativeNames = WindowsMonitorNameProvider.GetNames();
        var screen = window.Screens.All.FirstOrDefault(candidate =>
                     {
                         nativeNames.TryGetValue(candidate.Bounds.Position, out var nativeName);
                         var displayName = string.IsNullOrWhiteSpace(candidate.DisplayName)
                             ? nativeName
                             : candidate.DisplayName;
                         return !string.IsNullOrWhiteSpace(settings.EnabledMonitor)
                                && NotificationMonitorIdentifier.Matches(
                                    displayName,
                                    candidate.Bounds,
                                    settings.EnabledMonitor);
                     })
                     ?? window.Screens.Primary
                     ?? window.Screens.All.FirstOrDefault();
        if (screen is null)
            return;

        var scale = window.RenderScaling;
        var width = (int)Math.Round(window.Bounds.Width * scale);
        var height = (int)Math.Round(window.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
            return;

        var area = screen.WorkingArea;
        var x = settings.WindowPosition switch
        {
            1 or 2 => area.Center.X - width / 2,
            3 => area.X,
            4 => area.Right - width,
            _ => area.Center.X - width / 2
        };
        var y = settings.WindowPosition switch
        {
            1 => area.Y,
            2 => area.Bottom - height,
            3 or 4 => area.Center.Y - height / 2,
            _ => area.Center.Y - height / 2
        };
        window.Position = new PixelPoint(
            Math.Clamp(x + settings.HorizontalOffset, area.X, Math.Max(area.X, area.Right - width)),
            Math.Clamp(y + settings.VerticalOffset, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private static void ApplyQuickDrawWindowBounds(Window? window)
    {
        if (window is null)
            return;

        var screen = window.Screens.ScreenFromWindow(window);
        if (screen is null)
            return;

        const double edgePadding = 32;
        var scale = window.RenderScaling;
        window.MaxWidth = Math.Max(window.MinWidth, screen.WorkingArea.Width / scale - edgePadding);
        window.MaxHeight = Math.Max(window.MinHeight, screen.WorkingArea.Height / scale - edgePadding);
    }

    private static void CenterQuickDrawWindow(Window? window)
    {
        if (window is null)
            return;

        var screen = window.Screens.ScreenFromWindow(window);
        if (screen is null)
            return;

        var scale = window.RenderScaling;
        var width = (int)Math.Round(window.Bounds.Width * scale);
        var height = (int)Math.Round(window.Bounds.Height * scale);
        if (width <= 0 || height <= 0)
            return;

        var area = screen.WorkingArea;
        window.Position = new PixelPoint(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2);
    }

    #endregion

    #region TrayIcon

    private void MainTaskBarIconOnClicked(object? sender, EventArgs e)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var taskBarIconService = IAppHost.Host!.Services
            .GetServices<IHostedService>().OfType<TaskBarIconService>().First();

        var impl = typeof(TrayIcon)
            .GetProperty("Impl", BindingFlags.NonPublic | BindingFlags.Instance)?
            .GetValue(taskBarIconService.MainTaskBarIcon) as ITrayIconImpl;

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.FullName?.StartsWith(@"Avalonia.Win32") ?? false)
            .SelectMany(a => a.GetTypes())
            .FirstOrDefault(t => t.Name == "TrayIconImpl");

        if (impl == null || type == null) return;

        var methodInfo = type.GetMethod("OnRightClicked",
            BindingFlags.NonPublic | BindingFlags.Instance);
        methodInfo?.Invoke(impl, null);
    }

    private void MenuItemAbout_OnClick(object? sender, EventArgs e)
    {
        ShowSettingsWindow(@"settings.about");
    }

    private void MenuItemOpenMainWindow_OnClick(object? sender, EventArgs e)
    {
        ToggleMainWindow();
    }

    private void MenuItemToggleFloatingWindow_OnClick(object? sender, EventArgs e)
    {
        ToggleFloatingWindow();
    }

    internal static void RestoreAndActivate(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            if (window is MainWindow mainWindow)
                mainWindow.RestoreFromMinimized();
            else
                window.WindowState = WindowState.Normal;
        }

        window.Show();
        window.Activate();
    }

    internal static void RestoreWithoutActivating(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Show();
    }

    private void RefreshTrayWindowMenuItems()
    {
        if (_floatingWindowMenuItem is not null)
            _floatingWindowMenuItem.Header = _floatingWindow is { IsVisible: true }
                ? SecRandom.Langs.Common.Resources.Menu_HideFloatingWindow
                : SecRandom.Langs.Common.Resources.Menu_ShowFloatingWindow;
    }

    private void MenuItemOpenSettings_OnClick(object? sender, EventArgs e)
    {
        ShowSettingsWindow();
    }

    private void MenuItemRestartProgram_OnClick(object? sender, EventArgs e)
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.RestartApplication,
            () =>
            {
                Restart();
                return Task.CompletedTask;
            }), "Restart authorization failed.");
    }

    private void MenuItemExitProgram_OnClick(object? sender, EventArgs e)
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.ExitApplication,
            () =>
            {
                Stop();
                return Task.CompletedTask;
            }), "Exit authorization failed.");
    }

    #endregion
}
