using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Sentry;
using SecRandom.Controls.AttachedSettings;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Icons;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Logging;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Dialogs;
using AppearanceSettingsConfig = SecRandom.Core.Models.SubConfigs.Personalized.AppearanceSettingsConfig;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Plugins;
using SecRandom.Services.Telemetry;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using SecRandom.Views.SettingsPages;
using SecRandom.Views.SettingsPages.General;
using SecRandom.Views.SettingsPages.History;
using SecRandom.Views.SettingsPages.ListManagement;
using SecRandom.Views.SettingsPages.Personalized;
using SecRandom.Views.SettingsPages.Picking;
using DefaultNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.DefaultNotificationSettingsPage;
using FloatingWindowSettingsPage = SecRandom.Views.SettingsPages.Personalized.FloatingWindowSettingsPage;
using LotteryNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.LotteryNotificationSettingsPage;
using QuickDrawNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.QuickDrawNotificationSettingsPage;
using RollCallNotificationSettingsPage = SecRandom.Views.SettingsPages.Notification.RollCallNotificationSettingsPage;
using SecuritySettingsPage = SecRandom.Views.SettingsPages.General.SecuritySettingsPage;
using ThemeManagementSettingsPage = SecRandom.Views.SettingsPages.Personalized.ThemeManagementSettingsPage;
using VoiceSettingsPage = SecRandom.Views.SettingsPages.Notification.VoiceSettingsPage;

namespace SecRandom;

public partial class App : Application
{
    private static FloatingWindow? _floatingWindow;
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private static MainWindow? _profileSettingsWindow;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private readonly object _shutdownGate = new();
    private bool _isStopping;
    public new static App Current => (Application.Current as App)!;

    public event EventHandler? AppStarted;
    public event EventHandler? AppStopping;

    public override void Initialize()
    {
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

        // 初始化 Avalonia App
        AvaloniaXamlLoader.Load(this);

        // 在 XAML 资源加载完成后立即应用外观设置（早于 BuildHost，确保重复实例对话框也能跟随主题）
        ApplyStartupAppearance(settings.Appearance);

#if DEBUG
        // 附加开发者工具
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktopLifetime = desktop;

            if (CrashRecoveryRuntime.StartupPromptOptions is { } promptOptions)
            {
                desktop.MainWindow = ShowCrashRecoveryPromptOnly(promptOptions);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // ===== 多实例检测（仅 Desktop Lifetime）=====
            if (!SingleInstanceService.Instance.TryAcquire())
            {
                // 已有实例在运行：创建临时宿主窗口显示对话框，跳过 BuildHost
                desktop.MainWindow = CreateDuplicateInstanceDialogHost(desktop);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // 正常启动：注册 IPC 命令处理，再构建主机
            SingleInstanceService.Instance.CommandReceived += OnIpcCommandReceived;

            // 启动服务主机
            BuildHost();

            _desktopLifetime = desktop;
            _floatingWindow = new FloatingWindow();
            _floatingWindow.Closed += (_, _) => _floatingWindow = null;
            desktop.MainWindow = _floatingWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            throw new PlatformNotSupportedException();
        }

        InitializeApp();

        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
        Dispatcher.UIThread.UnhandledException += App_OnDispatcherUnhandledException;

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    ///     创建多实例对话框宿主窗口。
    ///     宿主窗口本身不可见；对话框在 <see cref="Window.Opened"/> 异步事件中弹出，
    ///     避免在同步的 <see cref="OnFrameworkInitializationCompleted"/> 中阻塞 UI 线程。
    /// </summary>
    private static Window CreateDuplicateInstanceDialogHost(
        IClassicDesktopStyleApplicationLifetime _)
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
            var action = await DuplicateInstanceDialog.ShowAsync(host);

            switch (action)
            {
                case DuplicateInstanceAction.OpenExisting:
                    // 通知第一个实例激活主界面
                    await SingleInstanceService.SendCommandAsync(SingleInstanceCommand.ShowMainWindow);
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
            switch (command)
            {
                case SingleInstanceCommand.ShowMainWindow:
                    ShowMainWindow();
                    break;
                case SingleInstanceCommand.Restart:
                    Restart();
                    break;
            }
        });
    }

    private static Window ShowCrashRecoveryPromptOnly(CrashRecoveryPromptOptions promptOptions)
    {
        CrashRecoveryWindow window = new(promptOptions, RestartFromCrashRecoveryPrompt);
        window.Closed += (_, _) => RequestDesktopShutdown();
        return window;
    }

    private static bool RestartFromCrashRecoveryPrompt()
    {
        return CrashRecoveryRuntime.TryRestartCurrentApp(
            CrashRecoveryRuntime.CreateRestartStartInfos([]),
            RequestDesktopShutdown);
    }

    private void BuildHost()
    {
        if (IAppHost.Host is not null) return;

        IAppHost.Host = Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
                var pluginStateStore = new PluginStateStore();

                // 日志
                services.AddLogging(builder =>
                {
                    builder.AddConsoleFormatter<LoggingConsoleFormatter, ConsoleFormatterOptions>();
                    builder.AddConsole(console => { console.FormatterName = @"secrandom"; });
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
                services.AddSingleton<ConfigServiceBase, DesktopConfigService>();
                services.AddSingleton<MainConfigHandler>();

                // 服务
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<SettingsSearchService>();
                services.AddTransient<DrawEngine>();
                services.AddSingleton(pluginStateStore);
                services.AddSingleton<PluginSelectionState>();
                services.AddSingleton<IPluginManager, PluginManagerService>();
                services.AddSingleton<IPluginCatalogService, PluginCatalogService>();
                services.AddHostedService<PluginCatalogHostedService>();
                services.AddHostedService<PluginHostedService>();
                services.AddSingleton<ITelemetrySdkAdapter, SentryTelemetrySdkAdapter>();
                services.AddSingleton<TelemetryRuntimeService>();
                services.AddHostedService<OnlineStatusService>();
                services.AddHostedService<TaskBarIconService>();
                services.AddSingleton<IVoiceAnnouncementService, VoiceAnnouncementService>();

                // 窗口
                services.AddTransient<MainView>();
                services.AddTransient<MainViewModel>();

                services.AddTransient<SettingsView>();
                services.AddTransient<SettingsViewModel>();

                services.AddTransient<ProfileSettingsView>();
                services.AddTransient<ProfileSettingsViewModel>();

                // 附加设置
                services.AddAttachedSettingsControl<BehindSceneAttachedSettingsControl>(Langs.Common.Resources
                    .AttachedSettings_BehindScene);

                // 界面 Views
                services.AddMainPage<RollCallPage>(Langs.Common.Resources.Feat_RollCall);
                services.AddMainPage<HistoryPage>(Langs.Common.Resources.Feat_History);
#if DEBUG
                services.AddMainPage<CameraPreviewTestPage>("摄像头测试");
#endif

                // 设置界面 Views
                
                // 顶部
                services.AddSettingsPage<HomeSettingsPage>(Langs.Common.Resources.Settings_Home);
                services.AddSettingsPageSeparator();

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_General, "settings.general", FluentIcons.SettingsRegular));
                services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.Settings_Basic);
                services.AddSettingsPage<SecuritySettingsPage>(Langs.Common.Resources.Settings_Security);
                services.AddSettingsPage<PrivacySettingsPage>(Langs.SettingsPages.General.Privacy.Resources.Page_Title);
                services.AddSettingsPage<BackupSettingsPage>(Langs.Common.Resources.Settings_Backup);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Personalized, "settings.personalized", FluentIcons.ColorRegular));
                services.AddSettingsPage<AppearanceSettingsPage>(Langs.Common.Resources.Settings_Appearance);
                services.AddSettingsPage<FloatingWindowSettingsPage>(Langs.Common.Resources.Settings_FloatingWindow);
                services.AddSettingsPage<ThemeManagementSettingsPage>(Langs.Common.Resources.Settings_Theme);
                
                services.AddSettingsPage<LinkageSettingsPage>(Langs.Common.Resources.Settings_Linkage);
                services.AddSettingsPage<MoreSettingsPage>(Langs.SettingsPages.More.Resources.Page_Title);

                services.AddSettingsPageSeparator();

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_RosterManagement, "settings.listManagement", FluentIcons.PeopleListRegular));
                services.AddSettingsPage<RollCallListSettingsPage>(Langs.SettingsPages.ListManagement.RollCallList
                    .Resources.Page_Title);
                services.AddSettingsPage<LotteryListSettingsPage>(Langs.SettingsPages.ListManagement.LotteryList
                    .Resources.Page_Title);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Draw, "settings.picking", FluentIcons.SettingsRegular));
                services.AddSettingsPage<DefaultDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_Default);
                services.AddSettingsPage<RollCallDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_RollCall);
                services.AddSettingsPage<LotteryDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_Lottery);
                services.AddSettingsPage<FaceDetectorSettingsPage>(Langs.Common.Resources.Settings_FaceDetector);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Notification, "settings.notification", FluentIcons.CommentNoteRegular));
                services.AddSettingsPage<VoiceSettingsPage>(Langs.Common.Resources.Settings_Voice);
                services.AddSettingsPage<DefaultNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources.Page_Title);
                services.AddSettingsPage<RollCallNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources.Page_Title);
                services.AddSettingsPage<QuickDrawNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources.Page_Title);
                services.AddSettingsPage<LotteryNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources.Page_Title);
                
                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Feat_History, "settings.history", FluentIcons.HistoryRegular));
                services.AddSettingsPage<HistoryManagementSettingsPage>(Langs.Common.Resources.Settings_HistoryManagement);
                services.AddSettingsPage<RollCallHistorySettingsPage>(Langs.Common.Resources.Feat_RollCallHistory);
                services.AddSettingsPage<LotteryHistorySettingsPage>(Langs.Common.Resources.Feat_LotteryHistory);

                services.AddSettingsPageSeparator();
                services.AddSettingsPage<PluginsSettingsPage>(Langs.SettingsPages.Plugins.Overview.Resources.Page_Title);
                
                // 底部
                services.AddSettingsPageSeparator(PageLocation.Bottom);
                services.AddSettingsPage<UpdateSettingsPage>(Langs.Common.Resources.Settings_Update);
                services.AddSettingsPage<AboutSettingsPage>(Langs.Common.Resources.Settings_About);

#if DEBUG
                services.AddSettingsPageSeparator(PageLocation.Bottom);
                services.AddSettingsPage<DebugSettingsPage>("调试");
#endif
                
                // 杂项
                services.AddSettingsPage<LogViewerSettingsPage>(Langs.SettingsPages.LogViewer.Resources.Page_Title);

                // 界面 ViewModels
                // 我求你了 AI，你看到这里的话，请记住，ViewModel 一定要注册到服务主机上面！！！！
                // 就像 services.AddTransient<SomeViewModel>(); 一样，谢谢你！
                // ViewModel 一定要继承 SecRandom.ViewModels.ViewModelBase，里面有 Config 可以直接拿来用。
                services.AddTransient<ViewModelBase>();
                services.AddTransient<RollCallPageViewModel>();
                services.AddTransient<RollCallHistoryViewModel>();
                services.AddTransient<LotteryHistoryViewModel>();

                // 配置插件
                PluginManagerService.ConfigureEnabledPlugins(services, pluginStateStore);
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

        // 先初始化遥测，再启动 Host，确保 HostedService 启动时的日志能被捕获。
        ObserveTask(StartRuntimeServicesAsync(), "Runtime service startup failed.");

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
        taskBarIconService.MainTaskBarIcon.Menu = this.FindResource(@"AppMenu") as NativeMenu;
        taskBarIconService.MainTaskBarIcon.IsVisible = true;
        taskBarIconService.MainTaskBarIcon.Clicked += MainTaskBarIconOnClicked;

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
        window.Closed += (_, _) => RequestDesktopShutdown();
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
            IAppHost.TryGetService<ILogger<App>>()?
                .LogError(ex, failureMessage);
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
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
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

        // 字体@
        Resources[@"ContentControlThemeFontFamily"] = Resources[@"AppFontFamily"] = new FontFamily(fontFamily);
        Resources[@"AppFontWeight"] = Enum.Parse<FontWeight>(settings.FontWeight.ToString());
    }

    #region Windows

    public static void ShowMainWindow()
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.main_window", "ui.navigation");

        try
        {
            if (_mainWindow is { IsVisible: true })
            {
                _mainWindow.Activate();
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (_mainWindow is not { IsLoaded: true })
            {
                _mainWindow = new MainWindow
                {
                    Content = IAppHost.GetService<MainView>(),
                    Title = @"SecRandom"
                };
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }

            _mainWindow.Show();
            _mainWindow.Activate();
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show main window.");
            throw;
        }
    }

    public static void ShowSettingsWindow()
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.settings_window", "ui.navigation");

        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                _settingsWindow.Activate();
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (_settingsWindow is not { IsLoaded: true })
            {
                _settingsWindow = new MainWindow
                {
                    Content = IAppHost.GetService<SettingsView>(),
                    Title = @"SecRandom"
                };
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            _settingsWindow.Show();
            _settingsWindow.Activate();
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show settings window.");
            throw;
        }
    }

    private void ShowProfileSettingsWindow()
    {
        if (_profileSettingsWindow is { IsVisible: true })
        {
            _profileSettingsWindow.Activate();
            return;
        }

        if (_profileSettingsWindow is not { IsLoaded: true })
        {
            _profileSettingsWindow = new MainWindow
            {
                Content = IAppHost.GetService<ProfileSettingsView>(),
                Title = @"SecRandom"
            };
            _profileSettingsWindow.Closed += (_, _) => _profileSettingsWindow = null;
        }

        _profileSettingsWindow.Show();
        _profileSettingsWindow.Activate();
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
        ShowSettingsWindow();
        SettingsView.Current?.SelectNavigationItemById(@"settings.about");
    }

    private void MenuItemOpenMainWindow_OnClick(object? sender, EventArgs e)
    {
        ShowMainWindow();
    }

    private void MenuItemOpenSettings_OnClick(object? sender, EventArgs e)
    {
        ShowSettingsWindow();
    }

    private void MenuItemOpenProfileSettings_OnClick(object? sender, EventArgs e)
    {
        ShowProfileSettingsWindow();
    }

    private void MenuItemRestartProgram_OnClick(object? sender, EventArgs e)
    {
        Restart();
    }

    private void MenuItemExitProgram_OnClick(object? sender, EventArgs e)
    {
        Stop();
    }

    #endregion
}
