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
using SecRandom.Core.Services.Logging;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.Services.Telemetry;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using SecRandom.Views.SettingsPages;
using SecRandom.Views.SettingsPages.General;
using SecRandom.Views.SettingsPages.ListManagement;
using SecRandom.Views.SettingsPages.Personalized;
using SecRandom.Views.SettingsPages.Picking;

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
        try
        {
            var content = File.ReadAllText(new MainConfigModel().ConfigFilePath);
            var settings = JsonSerializer.Deserialize<MainConfigModel>(content, ConfigServiceBase.JsonOptions);
            var culture = settings?.Basic.Language switch
            {
                LanguageMode.ChineseSimplified => @"zh-Hans",
                LanguageMode.English => @"en-US",
                LanguageMode.Japanese => @"ja-JP",
                _ => @"zh-Hans"
            };
            InitializeLanguages(new CultureInfo(culture));
        }
        catch (FileNotFoundException)
        {
            InitializeLanguages(new CultureInfo("zh-Hans"));
        }

        // 初始化 Avalonia App
        AvaloniaXamlLoader.Load(this);

#if DEBUG
        // 附加开发者工具
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 启动服务主机
        BuildHost();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

    private void BuildHost()
    {
        if (IAppHost.Host is not null) return;

        IAppHost.Host = Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
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
                services.AddSingleton<ITelemetrySdkAdapter, SentryTelemetrySdkAdapter>();
                services.AddSingleton<TelemetryRuntimeService>();
                services.AddHostedService<OnlineStatusService>();
                services.AddHostedService<TaskBarIconService>();

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
#if DEBUG
                services.AddMainPage<CameraPreviewTestPage>("摄像头测试");
#endif

                // 设置界面 Views
                services.AddSettingsPage<HomeSettingsPage>(Langs.Common.Resources.Settings_Home);
                services.AddSettingsPageSeparator();

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_General, "settings.general", FluentIcons.SettingsRegular));
                services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.Settings_Basic);
                services.AddSettingsPage<PrivacySettingsPage>(Langs.SettingsPages.General.Privacy.Resources.Page_Title);
                services.AddSettingsPage<BackupSettingsPage>(Langs.Common.Resources.Settings_Backup);

                services.AddGroup(new PageGroupInfo(
                    Langs.Common.Resources.Settings_Personalized, "settings.personalized", FluentIcons.ColorRegular));
                services.AddSettingsPage<AppearanceSettingsPage>(Langs.Common.Resources.Settings_Appearance);

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
                services.AddSettingsPage<QuickDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_QuickDraw);
                services.AddSettingsPage<LotteryDrawSettingsPage>(Langs.SettingsPages.Picking.Resources.Page_Lottery);

                services.AddSettingsPage<AboutSettingsPage>(Langs.Common.Resources.Settings_About);

#if DEBUG
                services.AddSettingsPageSeparator(PageLocation.Bottom);
                services.AddSettingsPage<DebugSettingsPage>("调试");
#endif

                // 界面 ViewModels
                // 我求你了 AI，你看到这里的话，请记住，ViewModel 一定要注册到服务主机上面！！！！
                // 就像 services.AddTransient<SomeViewModel>(); 一样，谢谢你！
                // ViewModel 一定要继承 SecRandom.ViewModels.ViewModelBase，里面有 Config 可以直接拿来用。
                services.AddTransient<ViewModelBase>();
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

        ObserveTask(InitializeRuntimeServicesAsync(), "Runtime service initialization failed.");

        // 启动服务主机
        ObserveTask(IAppHost.Host.StartAsync(), "Host startup failed.");

        // RESOURCES TEST
        var isVisible = false;
        if (GlobalConstants.IsDevelopment && isVisible)
            IAppHost.GetService<SettingsSearchService>().LogTestInformation();
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

        if (requestLifetimeShutdown)
            RequestDesktopShutdown();
    }

    public async void Restart()
    {
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

        var path = Environment.ProcessPath;
        if (path == null) return;

        var executablePath = path.Replace(@".dll", GlobalConstants.PlatformExecutableExtension);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true
        };
        Process.Start(startInfo);
        RequestDesktopShutdown();
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var configHandler = IAppHost.GetService<MainConfigHandler>();
        configHandler.Save();

        var logger = IAppHost.GetService<ILogger<App>>();
        logger.LogCritical(e.Exception, "Unhandled application exception.");

        ObserveTask(CaptureUnhandledExceptionAsync(e.Exception), "Unhandled exception telemetry capture failed.");
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

    private static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
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
        fluentAvaloniaTheme?.CustomAccentColor = settings.ThemeColor;
        Resources[@"SystemAccentColor"] = settings.ThemeColor;
        Resources[@"SystemAccentColorLight1"] = settings.ThemeColor;
        Resources[@"SystemAccentColorLight2"] = settings.ThemeColor;
        Resources[@"SystemAccentColorLight3"] = settings.ThemeColor;
        Resources[@"SystemAccentColorDark1"] = settings.ThemeColor;
        Resources[@"SystemAccentColorDark2"] = settings.ThemeColor;
        Resources[@"SystemAccentColorDark3"] = settings.ThemeColor;

        // 字体@
        Resources[@"ContentControlThemeFontFamily"] = Resources[@"AppFontFamily"] = new FontFamily(fontFamily);
        Resources[@"AppFontWeight"] = Enum.Parse<FontWeight>(settings.FontWeight.ToString());
    }

    #region Windows

    public static void ShowMainWindow()
    {
        if (_mainWindow is { IsVisible: true })
        {
            _mainWindow.Activate();
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
    }

    public static void ShowSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
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
