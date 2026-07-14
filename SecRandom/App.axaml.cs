using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
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
using SecRandom.Core.Services.Verification;
using SecRandom.Core.Services.Logging;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Shared.Models.Ipc;
using SecRandom.Dialogs;
using AppearanceSettingsConfig = SecRandom.Core.Models.SubConfigs.Personalized.AppearanceSettingsConfig;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Desktop;
using SecRandom.Services.Draw;
using SecRandom.Services.Notification;
using SecRandom.Services.Plugins;
using SecRandom.Services.Profiles;
using SecRandom.Services.Ipc;
using SecRandom.Services.ImportExport;
using SecRandom.Services.Settings;
using SecRandom.Services.Security;
using SecRandom.Services.Telemetry;
using SecRandom.Services.Verification;
using SecRandom.Services.Voice;
using SecRandom.ViewModels;
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
using SecRandom.Views.SettingsPages.Plugins.Overview;
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
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private static MainWindow? _profileSettingsWindow;
    private NativeMenuItem? _floatingWindowMenuItem;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    private readonly object _shutdownGate = new();
    private bool _isStopping;
    public new static App Current => (Application.Current as App)!;
    internal bool IsStopping => _isStopping;

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
        var startupProtocolUri = ProtocolActivation.ConsumeStartupUri();
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
                desktop.MainWindow = CreateDuplicateInstanceDialogHost(desktop, startupProtocolUri);
                base.OnFrameworkInitializationCompleted();
                return;
            }

            // 正常启动：注册 IPC 命令处理，再构建主机
            SingleInstanceService.Instance.CommandReceived += OnIpcCommandReceived;
            SingleInstanceService.Instance.RequestReceived += OnIpcRequestReceived;

            // 启动服务主机
            BuildHost();

            _desktopLifetime = desktop;
            _floatingWindow = new FloatingWindow();
            _floatingWindow.Opened += (_, _) => RefreshTrayWindowMenuItems();
            _floatingWindow.Closed += (_, _) => _floatingWindow = null;
            if (!IAppHost.GetService<MainConfigHandler>().Data.FloatingWindowSettings.StartupDisplayFloatingWindow)
                _floatingWindow.Hide();
            desktop.MainWindow = _floatingWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            throw new PlatformNotSupportedException();
        }

        InitializeApp();
        if (startupProtocolUri is not null)
            Dispatcher.UIThread.Post(() => HandleProtocolUri(startupProtocolUri), DispatcherPriority.Render);

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
                var sent = await SingleInstanceService.SendCommandAsync(SingleInstanceCommand.UrlPrefix + startupProtocolUri);
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

    private Task<IpcResponseEnvelope> OnIpcRequestReceived(IpcRequestEnvelope request, CancellationToken cancellationToken)
    {
        return IAppHost.GetService<ProtocolCommandRouter>().HandleIpcAsync(request, cancellationToken);
    }

    private void HandleProtocolUri(string value)
    {
        ObserveTask(IAppHost.GetService<ProtocolCommandRouter>().HandleUrlAsync(value), "Protocol URL handling failed.");
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
                services.AddSingleton<IProfileQueryService, ProfileQueryService>();
                services.AddSingleton<IDrawTemporaryRecordService, DrawTemporaryRecordService>();
                services.AddSingleton<DrawProofExportService>();
                services.AddSingleton<IVerificationKernel, ManagedVerificationKernel>();
                services.AddHttpClient<IWitnessClient, WitnessClient>(client => client.Timeout = TimeSpan.FromSeconds(3));
                services.AddSingleton<WitnessTicketCache>();
                services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<WitnessTicketCache>());
                services.AddTransient<VerificationDrawCoordinator>();
                services.AddSingleton<SettingsSearchService>();
                services.AddSingleton<IImportExportService, Services.ImportExport.ImportExportService>();
                services.AddHostedService<AutomaticBackupService>();
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
                services.AddSingleton<GlobalShortcutService>();
                services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GlobalShortcutService>());
                services.AddSingleton<DesktopIntegrationService>();
                services.AddSingleton<ProtocolCommandRouter>();
                services.AddSingleton<IVoiceAnnouncementService, VoiceAnnouncementService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<DrawAudioService>();
                services.AddSingleton<ICredentialKeyProtector, CredentialKeyProtector>();
                services.AddSingleton<SecurityCredentialStore>();
                services.AddSingleton<ISecurityVerificationPrompt, SecurityVerificationPrompt>();
                services.AddSingleton<ISecurityService, SecurityService>();

                // 窗口
                services.AddTransient<MainView>();
                services.AddTransient<MainViewModel>();

                services.AddTransient<SettingsView>();
                services.AddTransient<SettingsViewModel>();

                services.AddTransient<ProfileSettingsView>();
                services.AddTransient<ProfileSettingsViewModel>();
                services.AddTransient<QuickDrawPage>();

                // 附加设置
                services.AddAttachedSettingsControl<BehindSceneAttachedSettingsControl>(Langs.Common.Resources
                    .AttachedSettings_BehindScene);
                services.AddAttachedSettingsControl<DrawImageAttachedSettingsControl>("展示图片");

                // 界面 Views
                services.AddMainPage<RollCallPage>(Langs.Common.Resources.Feat_RollCall);
                services.AddMainPage<LotteryPage>(Langs.Common.Resources.Feat_Lottery);
                services.AddMainPage<HistoryPage>(Langs.Common.Resources.Feat_History);

                // 设置界面 Views
                
                // 顶部
                services.AddSettingsPage<HomeSettingsPage>(Langs.Common.Resources.Settings_Home);
                services.AddSettingsPageSeparator();

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_General, "settings.general", FluentIcons.SettingsFilled));
                services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.Settings_Basic);
                services.AddSettingsPage<SecuritySettingsPage>(Langs.Common.Resources.Settings_Security);
                services.AddSettingsPage<PrivacySettingsPage>(Langs.SettingsPages.General.Privacy.Resources.Page_Title);
                services.AddSettingsPage<VerificationSettingsPage>(Langs.SettingsPages.General.Verification.Resources.Page_Title);
                services.AddSettingsPage<BackupSettingsPage>(Langs.Common.Resources.Settings_Backup);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Personalized, "settings.personalized", FluentIcons.ColorFilled));
                services.AddSettingsPage<AppearanceSettingsPage>(Langs.Common.Resources.Settings_Appearance);
                services.AddSettingsPage<FloatingWindowSettingsPage>(Langs.Common.Resources.Settings_FloatingWindow);
                
                services.AddSettingsPage<LinkageSettingsPage>(Langs.Common.Resources.Settings_Linkage);
                services.AddSettingsPage<MoreSettingsPage>(Langs.SettingsPages.More.Resources.Page_Title);

                services.AddSettingsPageSeparator();

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_RosterManagement, "settings.listManagement", FluentIcons.PeopleListFilled));
                services.AddSettingsPage<RollCallListSettingsPage>(Langs.SettingsPages.ListManagement.RollCallList
                    .Resources.Page_Title);
                services.AddSettingsPage<LotteryListSettingsPage>(Langs.SettingsPages.ListManagement.LotteryList
                    .Resources.Page_Title);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Draw, "settings.picking", FluentIcons.SettingsFilled));
                services.AddSettingsPage<DefaultDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_Default);
                services.AddSettingsPage<RollCallDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_RollCall);
                services.AddSettingsPage<QuickDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_QuickDraw);
                services.AddSettingsPage<LotteryDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_Lottery);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Notification, "settings.notification", FluentIcons.CommentNoteFilled));
                services.AddSettingsPage<VoiceSettingsPage>(Langs.Common.Resources.Settings_Voice);
                services.AddSettingsPage<DefaultNotificationSettingsPage>(Langs.SettingsPages.Notification.Resources.Page_Title);
                services.AddSettingsPage<RollCallNotificationSettingsPage>(Langs.Common.Resources.Settings_RollCallNotification);
                services.AddSettingsPage<QuickDrawNotificationSettingsPage>(Langs.Common.Resources.Settings_QuickDrawNotification);
                services.AddSettingsPage<LotteryNotificationSettingsPage>(Langs.Common.Resources.Settings_LotteryNotification);
                
                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Feat_History, "settings.history", FluentIcons.HistoryFilled));
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
                // Draw sessions are shared by UI and IPC. They must outlive a page navigation.
                services.AddSingleton<RollCallPageViewModel>();
                services.AddSingleton<QuickDrawPageViewModel>();
                services.AddSingleton<LotteryPageViewModel>();
                services.AddTransient<RollCallHistoryViewModel>();
                services.AddTransient<HomeSettingsPageViewModel>();
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
                RestoreAndActivate(_mainWindow);
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (_mainWindow is not { IsLoaded: true })
            {
                _mainWindow = new MainWindow(MainWindowSettingsScope.Primary)
                {
                    Content = IAppHost.GetService<MainView>(),
                    Title = @"SecRandom"
                };
                _mainWindow.Closed += (_, _) => _mainWindow = null;
            }

            RestoreAndActivate(_mainWindow);
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show main window.");
            throw;
        }
    }

    public static void ToggleMainWindow(string? pageId = null)
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeAsync(
            SecurityOperation.ToggleMainWindow,
            () =>
            {
                ShowMainWindow();
                if (!string.IsNullOrWhiteSpace(pageId))
                    MainView.Current?.SelectNavigationItemById(pageId);
                return Task.CompletedTask;
            }), "Main window authorization failed.");
    }

    public static void SetMainWindowVisibility(string action, string? pageId = null)
    {
        var shouldShow = action switch
        {
            "show" => true,
            "hide" => false,
            _ => _mainWindow is not { IsVisible: true }
        };
        if (shouldShow)
        {
            ShowMainWindow();
            if (!string.IsNullOrWhiteSpace(pageId))
                MainView.Current?.SelectNavigationItemById(pageId);
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
                    _floatingWindow.Hide();
                else if (_floatingWindow is not null)
                {
                    RestoreAndActivate(_floatingWindow);
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
            _ => _floatingWindow is not { IsVisible: true }
        };
        if (shouldShow && _floatingWindow is not null)
            RestoreAndActivate(_floatingWindow);
        else if (!shouldShow)
            _floatingWindow?.Hide();
        Current.RefreshTrayWindowMenuItems();
    }

    public static void ShowSettingsWindow()
    {
        ObserveTask(IAppHost.GetService<ISecurityService>().AuthorizeSettingsAsync(
            () =>
            {
                ShowSettingsWindowCore();
                SettingsView.Current?.ExitPreview();
                return Task.CompletedTask;
            },
            () =>
            {
                ShowSettingsWindowCore();
                SettingsView.Current?.NavigateToPreviewPage("settings.general.basic");
                return Task.CompletedTask;
            }), "Settings window authorization failed.");
    }

    public static void SetSettingsWindowVisibility(string action, string pageId, bool preview)
    {
        var shouldShow = action switch
        {
            "show" => true,
            "hide" => false,
            _ => _settingsWindow is not { IsVisible: true }
        };
        if (shouldShow)
        {
            ShowSettingsWindowCore();
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

    private static void ShowSettingsWindowCore()
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.settings_window", "ui.navigation");

        try
        {
            if (_settingsWindow is { IsVisible: true })
            {
                RestoreAndActivate(_settingsWindow);
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (_settingsWindow is not { IsLoaded: true })
            {
                _settingsWindow = new MainWindow(MainWindowSettingsScope.Settings)
                {
                    Content = IAppHost.GetService<SettingsView>(),
                    Title = @"SecRandom"
                };
                _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            }

            RestoreAndActivate(_settingsWindow);
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
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        using var transaction = telemetry?.StartTransaction("ui.quick_draw_window", "ui.navigation");

        try
        {
            if (_quickDrawWindow is { IsVisible: true })
            {
                _quickDrawWindow.Activate();
                transaction?.Finish(SpanStatus.Ok);
                return;
            }

            if (_quickDrawWindow is not { IsLoaded: true })
            {
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
                        () => CenterQuickDrawWindow(_quickDrawWindow),
                        DispatcherPriority.Render);
                };
                _quickDrawWindow.SizeChanged += (_, _) => Dispatcher.UIThread.Post(
                    () => CenterQuickDrawWindow(_quickDrawWindow),
                    DispatcherPriority.Render);
                _quickDrawWindow.PositionChanged += (_, _) => ApplyQuickDrawWindowBounds(_quickDrawWindow);
                _quickDrawWindow.ScalingChanged += (_, _) => ApplyQuickDrawWindowBounds(_quickDrawWindow);
                _quickDrawWindow.Closed += (_, _) => _quickDrawWindow = null;
            }

            _quickDrawWindow.Show();
            _quickDrawWindow.Activate();
            (_quickDrawWindow.Content as QuickDrawPage)?.StartDraw();
            transaction?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            transaction?.Finish(ex, SpanStatus.InternalError);
            IAppHost.TryGetService<ILogger<App>>()?.LogError(ex, "Failed to show quick draw window.");
            throw;
        }
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
        ToggleMainWindow();
    }

    private void MenuItemToggleFloatingWindow_OnClick(object? sender, EventArgs e)
    {
        ToggleFloatingWindow();
    }

    private static void RestoreAndActivate(Window window)
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

    private void MenuItemOpenProfileSettings_OnClick(object? sender, EventArgs e)
    {
        ShowProfileSettingsWindow();
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
