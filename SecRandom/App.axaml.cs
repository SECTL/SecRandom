using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SecRandom.Controls.AttachedSettings;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Logging;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using SecRandom.Views.SettingsPages;

namespace SecRandom;

public partial class App : Application
{
    public new static App Current => (Application.Current as App)!;
    
    private static FloatingWindow? _floatingWindow;
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private static MainWindow? _profileSettingsWindow;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        InitializeLanguages(new CultureInfo("zh-hans"));
        // InitializeLanguages(new CultureInfo("en-us"));
        BuildHost();
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            _desktopLifetime = desktop;
            _floatingWindow = new FloatingWindow();
            _floatingWindow.Closed += (_, _) => _floatingWindow = null;
            desktop.MainWindow = _floatingWindow;
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            throw new PlatformNotSupportedException();
        }
        
        AppDomain.CurrentDomain.ProcessExit += CurrentDomainOnProcessExit;
        Dispatcher.UIThread.UnhandledException += App_OnDispatcherUnhandledException;
        
        base.OnFrameworkInitializationCompleted();
    }

    private static void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
    
    private static void BuildHost()
    {
        if (IAppHost.Host is not null)
        {
            return;
        }

        IAppHost.Host = Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices(services =>
            {
                // 日志
                services.AddLogging(builder =>
                {
                    builder.AddConsoleFormatter<LoggingConsoleFormatter, ConsoleFormatterOptions>();
                    builder.AddConsole(console => { console.FormatterName = "secrandom"; });
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
                
                // 窗口
                services.AddTransient<MainView>();
                services.AddTransient<MainViewModel>();
                
                services.AddTransient<SettingsView>();
                services.AddTransient<SettingsViewModel>();

                services.AddTransient<ProfileSettingsView>();
                services.AddTransient<ProfileSettingsViewModel>();
                
                // 附加设置
                services.AddAttachedSettingsControl<BehindSceneAttachedSettingsControl>(Langs.Common.Resources.AttachedSettings_BehindScene);
                
                // 界面 Views
                services.AddMainPage<RollCallPage>(Langs.Common.Resources.Feat_RollCall);
                
                // 设置界面 Views
                services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.Settings_Basic);
                services.AddSettingsPage<BackupSettingsPage>(Langs.Common.Resources.Settings_Backup);

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
        
        logger.LogInformation("SecRandom {VERSION} (Codename: {CODENAME})",
            GlobalConstants.Version, GlobalConstants.CodeName);
        logger.LogInformation("Copyright by SECTL(2025~{YEAR})  Licensed under GPL3.0", DateTime.Now.Year);
        logger.LogInformation("Host built.");
        
        var lifetime = IAppHost.GetService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(Stop);

        IAppHost.GetService<IProfileService>();
        
        // 启动服务主机
        _ = IAppHost.Host.StartAsync();
        
        // RESOURCES TEST
        if (true) return;
        
        var resources = Assembly.GetExecutingAssembly().DefinedTypes
            .Where(info => info.Namespace?.StartsWith("SecRandom.Langs.SettingsPages") ?? false)
            .OrderBy(info => info.FullName ?? "???")
            .ToList();
        using (logger.BeginScope("RESOURCES TEST"))
        {
            foreach (var resourceType in resources)
            {
                var settingsPageId = resourceType.FullName?.Split(".")[3];
                if (settingsPageId == null) continue;
                
                var settingsPageName =
                    typeof(Langs.Common.Resources).GetProperty("Settings_" + settingsPageId)?.GetValue(null) ?? "???";
                
                var properties = resourceType.DeclaredProperties;
                
                using var scope = logger.BeginScope($"[{settingsPageId}] {settingsPageName}");
                foreach (var declaredProperty in properties)
                {
                    if (declaredProperty.Name.StartsWith("S_") && !declaredProperty.Name.EndsWith("_R"))
                    {
                        logger.LogDebug("[{Name}] {Value}", declaredProperty.Name, declaredProperty.GetValue(null));
                    }
                }
            }
        }
    }

    public static void Stop()
    {
        var logger = IAppHost.GetService<ILogger<App>>();
        logger.LogInformation("正在停止应用");

        _floatingWindow?.CanClose = true;

        IAppHost.GetService<MainConfigHandler>().Save();
        IAppHost.GetService<IProfileService>().SaveProfile();
        
        IAppHost.Host?.StopAsync(TimeSpan.FromSeconds(5));
        _desktopLifetime?.Shutdown();
    }

    public static void Restart()
    {
        var path = Environment.ProcessPath;
        if (path == null) return;
        
        var executablePath = path.Replace(".dll", GlobalConstants.PlatformExecutableExtension);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = true
        };
        Process.Start(startInfo);
    }

    private void App_OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var configHandler = IAppHost.GetService<MainConfigHandler>();
        configHandler.Save();
        
        var logger = IAppHost.GetService<ILogger<App>>();
        logger.LogCritical(e.Exception, "发生严重错误");
    }

    private void CurrentDomainOnProcessExit(object? sender, EventArgs e)
    {
        var configHandler = IAppHost.GetService<MainConfigHandler>();
        configHandler.Save();
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
                Title = "SecRandom"
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
                Title = "SecRandom"
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
                Title = "SecRandom"
            };
            _profileSettingsWindow.Closed += (_, _) => _profileSettingsWindow = null;
        }

        _profileSettingsWindow.Show();
        _profileSettingsWindow.Activate();
    }

    #endregion

    private static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }

    #region TrayIcon

    private void MenuItemAbout_OnClick(object? sender, EventArgs e)
    {
        ShowSettingsWindow();
        SettingsView.Current?.SelectNavigationItemById("settings.about");
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

    private void MenuItemExitProgram_OnClick(object? sender, EventArgs e)
    {
        Stop();
    }

    #endregion
}
