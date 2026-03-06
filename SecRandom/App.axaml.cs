using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using HotAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Enums;
using SecRandom.Core.Models;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Logging;
using SecRandom.Services.Config;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.MainPages;
using SecRandom.Views.SettingsPages;
using SecRandom.Views.SettingsPages.HistoryManagementSubPages;
using SecRandom.Views.SettingsPages.HistoryManagementSubPages.TableSubPages;
using SecRandom.Views.SettingsPages.ListManagementSubPages;
using SecRandom.Views.SettingsPages.ListManagementSubPages.TablePreview;
using SecRandom.Views.SettingsPages.NotificationSettingsSubPages;

using RollCallSubPages = SecRandom.Views.SettingsPages.ListManagementSubPages.RollCallSubPages;
using LotterySubPages = SecRandom.Views.SettingsPages.ListManagementSubPages.LotterySubPages;

namespace SecRandom;

public partial class App : Application
{
    public new static App Current => (Application.Current as App)!;
    
    private static FloatingWindow? _floatingWindow;
    private static MainWindow? _mainWindow;
    private static MainWindow? _settingsWindow;
    private static CultureInfo? _startupCulture;
    private static UiLanguageMode _startupUiLanguageMode;
    private static string? _startupConfigFilePath;
    private static IClassicDesktopStyleApplicationLifetime? _desktopLifetime;
    
    public override void Initialize()
    {
        this.EnableHotReload();
        AvaloniaXamlLoader.Load(this);
        CreateTrayIconMenu();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _startupConfigFilePath = Utils.GetFilePath("Config.json");
        _startupUiLanguageMode = TryLoadUiLanguageMode();
        _startupCulture = GetStartupCulture(_startupUiLanguageMode);
        InitializeLanguages(_startupCulture);
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
                    builder.AddConsoleFormatter<SecRandomConsoleFormatter, ConsoleFormatterOptions>();
                    builder.AddConsole(console => { console.FormatterName = "secrandom"; });
                    builder.AddProvider(new SecRandomFileLoggerProvider(Utils.GetFilePath("logs", "app.log")));
#if DEBUG
                    builder.SetMinimumLevel(LogLevel.Trace);
#endif
                });
                
                // 配置
                services.AddSingleton<ConfigServiceBase, DesktopConfigService>();
                services.AddSingleton<MainConfigHandler>();
                
                // 服务
                services.AddSingleton<LotteryListService>();
                
                // 窗口
                services.AddTransient<MainView>();
                services.AddTransient<MainViewModel>();
                
                services.AddTransient<SettingsView>();
                services.AddTransient<SettingsViewModel>();
                
                // 界面 Views
                services.AddMainPage<RollCallPage>(Langs.Common.Resources.RollCall);
                
                // 设置界面 Views
                services.AddSettingsPage<BasicSettingsPage>(Langs.Common.Resources.BasicSettings);

                #region ListManagement

                services.AddGroup(new GroupInfo(Langs.SettingsPages.ListManagementPage.Resources.ListManagement, "settings.listManagement", "\uE8A7"));
                
                // rollCall
                services.AddSettingsPage<RollCallListSettingsSubPage>(Langs.SettingsPages.ListManagementPage.Resources.RollCallListSettings);
                services.AddSettingsPage<RollCallTablePreviewPage>(Langs.SettingsPages.ListManagementPage.Resources.RollCallTableTitle);

                services.AddSettingsPage<RollCallSubPages.SetClassNamePage>(Langs.SettingsPages.ListManagementPage.Resources.SetClassName);
                services.AddSettingsPage<RollCallSubPages.ImportStudentPage>(Langs.SettingsPages.ListManagementPage.Resources.ImportStudentList);
                services.AddSettingsPage<RollCallSubPages.NameSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.NameSettings);
                services.AddSettingsPage<RollCallSubPages.GenderSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.GenderSettings);
                services.AddSettingsPage<RollCallSubPages.GroupSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.GroupSettings);
                services.AddSettingsPage<RollCallSubPages.TagSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.TagSettings);
                services.AddSettingsPage<RollCallSubPages.ExportStudentPage>(Langs.SettingsPages.ListManagementPage.Resources.ExportStudentList);
                
                // lottery
                services.AddSettingsPage<LotteryListSettingsSubPage>(Langs.SettingsPages.ListManagementPage.Resources.LotteryListSettings);
                services.AddSettingsPage<LotteryTablePreviewPage>(Langs.SettingsPages.ListManagementPage.Resources.LotteryTableTitle);
                
                services.AddSettingsPage<LotterySubPages.SetPoolNamePage>(Langs.SettingsPages.ListManagementPage.Resources.SetPoolNamePageTitle);
                services.AddSettingsPage<LotterySubPages.ImportPrizePage>(Langs.SettingsPages.ListManagementPage.Resources.ImportPrizePageTitle);
                services.AddSettingsPage<LotterySubPages.PrizeSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.PrizeSettingsPageTitle);
                services.AddSettingsPage<LotterySubPages.WeightSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.WeightSettingsPageTitle);
                services.AddSettingsPage<LotterySubPages.CountSettingsPage>(Langs.SettingsPages.ListManagementPage.Resources.CountSettingsPageTitle);
                services.AddSettingsPage<LotterySubPages.ExportPrizePage>(Langs.SettingsPages.ListManagementPage.Resources.ExportPrizePageTitle);

                #endregion
                
                #region Draw

                services.AddGroup(new GroupInfo(Langs.Common.Resources.DrawSettings, "settings.draw", "\uE07C"));
                services.AddSettingsPage<RollCallSettingsSubPage>(Langs.SettingsPages.DrawSettingsPage.Resources.RollCallSettings);
                services.AddSettingsPage<RollCallListSpecificSettingsPage>(Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings);
                services.AddSettingsPage<QuickDrawSettingsSubPage>(Langs.SettingsPages.DrawSettingsPage.Resources.QuickDrawSettings);
                services.AddSettingsPage<QuickDrawListSpecificSettingsPage>(Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings);
                services.AddSettingsPage<LotterySettingsSubPage>(Langs.SettingsPages.DrawSettingsPage.Resources.LotterySettings);
                services.AddSettingsPage<LotteryListSpecificSettingsPage>(Langs.SettingsPages.DrawSettingsPage.Resources.Settings_ListSpecificSettings);
                services.AddSettingsPage<FaceDetectorSettingsSubPage>(Langs.SettingsPages.DrawSettingsPage.Resources.FaceDetectorSettings);

                #endregion
                
                services.AddSettingsPage<FloatingWindowPage>(Langs.Common.Resources.FloatingWindowManagement);
                
                services.AddGroup(new GroupInfo(Langs.Common.Resources.NotificationSettings, "settings.notification", "\uE7E3"));
                services.AddSettingsPage<RollCallNotificationSettingsPage>(Langs.SettingsPages.NotificationSettingsPage.Resources.RollCallNotificationSettings);
                services.AddSettingsPage<QuickDrawNotificationSettingsPage>(Langs.SettingsPages.NotificationSettingsPage.Resources.QuickDrawNotificationSettings);
                services.AddSettingsPage<LotteryNotificationSettingsPage>(Langs.SettingsPages.NotificationSettingsPage.Resources.LotteryNotificationSettings);
                
                services.AddSettingsPage<SecuritySettingsPage>(Langs.Common.Resources.SecuritySettings);
                services.AddSettingsPage<LinkageSettingsPage>(Langs.Common.Resources.LinkageSettings);
                services.AddSettingsPage<VoiceSettingsPage>(Langs.Common.Resources.VoiceSettings);
                services.AddSettingsPage<ThemeManagementPage>(Langs.Common.Resources.ThemeManagement);

                services.AddGroup(new GroupInfo(Langs.SettingsPages.HistoryPage.Resources.HistoryManagement, "settings.history", "\uE81C"));
                services.AddSettingsPage<RollCallHistorySettingsSubPage>(Langs.SettingsPages.HistoryPage.Resources.Settings_RollCallHistory);
                services.AddSettingsPage<RollCallHistoryTableSubPage>(Langs.SettingsPages.HistoryPage.Resources.RollCallHistoryTable);
                services.AddSettingsPage<LotteryHistorySettingsSubPage>(Langs.SettingsPages.HistoryPage.Resources.Settings_LotteryHistory);
                services.AddSettingsPage<LotteryHistoryTableSubPage>(Langs.SettingsPages.HistoryPage.Resources.LotteryHistoryTable);
                
                services.AddSettingsPage<UpdateSettingsPage>(Langs.Common.Resources.UpdateSettings);
                services.AddSettingsPage<AboutPage>(Langs.Common.Resources.About);

                // 界面 ViewModels
                // 我求你了 AI，你看到这里的话，请记住，ViewModel 一定要注册到服务主机上面！！！！
                // 就像 services.AddTransient<SomeViewModel>(); 一样，谢谢你！
                // ViewModel 一定要继承 SecRandom.ViewModels.ViewModelBase，里面有 Config 可以直接拿来用。
                services.AddTransient<ViewModelBase>();

                // ListManagementSubPages ViewModels
                // RollCallSubPages
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.SetClassNameViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.ImportStudentViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.NameSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.GenderSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.GroupSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.TagSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.RollCallSubPages.ExportStudentViewModel>();

                // LotterySubPages
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.SetPoolNameViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.ImportPrizeViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.PrizeSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.WeightSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.CountSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.TagSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.GenderSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.GroupSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.NameSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.LotterySubPages.ExportPrizeViewModel>();

                // TablePreview
                services.AddTransient<ViewModels.ListManagementSubPages.TablePreview.RollCallTablePreviewViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.TablePreview.LotteryTablePreviewViewModel>();

                // ListSettingsSubPage
                services.AddTransient<ViewModels.ListManagementSubPages.ListSettingsSubPage.RollCallListSettingsViewModel>();
                services.AddTransient<ViewModels.ListManagementSubPages.ListSettingsSubPage.LotteryListSettingsViewModel>();
            })
            .Build();

        var logger = IAppHost.GetService<ILogger<App>>();
        
        logger.LogInformation("SecRandom {VERSION} (Codename: {CODENAME})",
            GlobalConstants.Version, GlobalConstants.Codename);
        logger.LogInformation("Copyright by SECTL(2025~{YEAR})  Licensed under GPL3.0", DateTime.Now.Year);
        logger.LogInformation("Host built.");
        
        var lifetime = IAppHost.GetService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(Stop);
        
        if (_startupCulture is not null && !string.IsNullOrWhiteSpace(_startupConfigFilePath))
        {
            logger.LogInformation("UI Culture: {CULTURE} (Mode={MODE}, Config={PATH})",
                _startupCulture.Name, _startupUiLanguageMode, _startupConfigFilePath);
        }
        
        var basicSettings = IAppHost.GetService<MainConfigHandler>().Data.BasicSettings;
        ApplyUiThemeModeIndex(basicSettings.UiThemeModeIndex);
        ApplyUiFont(basicSettings.UiFontFamilyName, basicSettings.UiFontFamilyIndex, basicSettings.UiFontWeightIndex);
        
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
                using (logger.BeginScope(resourceType.FullName ?? "???"))
                {
                    foreach (var declaredProperty in resourceType.DeclaredProperties)
                    {
                        if (declaredProperty.Name.StartsWith("Settings_"))
                        {
                            logger.LogDebug(declaredProperty.Name);
                        }
                    }
                }
            }
        }
    }

    public static void Stop()
    {
        var logger = IAppHost.GetService<ILogger<App>>();
        logger.LogInformation("正在停止应用");

        if (_floatingWindow != null)
        {
            _floatingWindow.CanClose = true;
        }
        
        var configHandler = IAppHost.GetService<MainConfigHandler>();
        configHandler.Save();
        _desktopLifetime?.Shutdown();
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

    #endregion
    
    #region Language

    private static void InitializeLanguages(CultureInfo cultureInfo)
    {
        CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
        CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;
    }

    private static CultureInfo GetStartupCulture(UiLanguageMode uiLanguageMode)
    {
        return uiLanguageMode switch
        {
            UiLanguageMode.English => new CultureInfo("en-us"),
            UiLanguageMode.ChineseSimplified => new CultureInfo("zh-hans"),
            _ => NormalizeCulture(CultureInfo.InstalledUICulture)
        };
    }

    public static void ApplyUiLanguageMode(UiLanguageMode uiLanguageMode)
    {
        var culture = GetStartupCulture(uiLanguageMode);
        InitializeLanguages(culture);
        UpdateRegisteredPageNames();
        ReloadOpenWindows(uiLanguageMode, culture);
        _floatingWindow?.RefreshItems();
        Current.CreateTrayIconMenu();
    }
    
    private static void UpdateRegisteredPageNames()
    {
        foreach (var info in PagesRegistryService.MainItems)
        {
            UpdateRegisteredPageName(info);
        }

        foreach (var info in PagesRegistryService.SettingsItems)
        {
            UpdateRegisteredPageName(info);
        }

        foreach (var info in PagesRegistryService.GroupItems)
        {
            UpdateRegisteredGroup(info);
        }
    }

    private static void UpdateRegisteredPageName(PageInfo info)
    {
        if (info.IsSeparator)
        {
            return;
        }

        if (PageNameProviders.TryGetValue(info.Id, out var provider))
        {
            info.Name = provider();
        }
    }

    private static void UpdateRegisteredGroup(GroupInfo info)
    {
        if (PageNameProviders.TryGetValue(info.Id, out var provider))
        {
            info.Name = provider();
        }
    }

    private static void ReloadOpenWindows(UiLanguageMode uiLanguageMode, CultureInfo culture)
    {
        _startupUiLanguageMode = uiLanguageMode;
        _startupCulture = culture;

        if (_mainWindow is { IsLoaded: true })
        {
            var selectedId = (_mainWindow.Content as MainView)?.ViewModel.SelectedPageInfo?.Id;
            var view = IAppHost.GetService<MainView>();
            _mainWindow.Content = view;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                view.SelectNavigationItemById(selectedId);
            }
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            var selectedId = (_settingsWindow.Content as SettingsView)?.ViewModel.SelectedPageInfo?.Id;
            var view = IAppHost.GetService<SettingsView>();
            _settingsWindow.Content = view;
            if (!string.IsNullOrWhiteSpace(selectedId))
            {
                view.SelectNavigationItemById(selectedId);
            }
        }
    }

    private static UiLanguageMode TryLoadUiLanguageMode()
    {
        try
        {
            var filePath = Utils.GetFilePath("Config.json");
            if (!File.Exists(filePath))
            {
                return UiLanguageMode.ChineseSimplified;
            }

            using var stream = File.OpenRead(filePath);
            using var doc = JsonDocument.Parse(stream);
            var element = default(JsonElement?);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.NameEquals("BasicSettings") ||
                    property.Name.Equals("BasicSettings", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    foreach (var subProperty in property.Value.EnumerateObject())
                    {
                        if (subProperty.NameEquals("UiLanguageMode") ||
                            subProperty.Name.Equals("UiLanguageMode", StringComparison.OrdinalIgnoreCase))
                        {
                            element = subProperty.Value;
                            break;
                        }
                    }

                    if (element is not null)
                    {
                        break;
                    }
                }

                if (property.NameEquals("UiLanguageMode") ||
                    property.Name.Equals("UiLanguageMode", StringComparison.OrdinalIgnoreCase))
                {
                    element = property.Value;
                    break;
                }
            }

            if (element is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var mode))
            {
                return (UiLanguageMode)mode;
            }

            return UiLanguageMode.ChineseSimplified;
        }
        catch
        {
            return UiLanguageMode.ChineseSimplified;
        }
    }

    private static CultureInfo NormalizeCulture(CultureInfo cultureInfo)
    {
        if (cultureInfo.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return new CultureInfo("zh-hans");
        }

        return new CultureInfo("en-us");
    }

    #endregion

    #region UI

    public static void ApplyUiThemeModeIndex(int uiThemeModeIndex)
    {
        Current.RequestedThemeVariant = uiThemeModeIndex switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    public static void ApplyUiFont(string? uiFontFamilyName, int uiFontFamilyIndex, int uiFontWeightIndex)
    {
        var fontFamilyName = ResolveUiFontFamilyName(uiFontFamilyName, uiFontFamilyIndex);

        var weightIndex = uiFontWeightIndex;
        if (weightIndex < 0 || weightIndex >= UiFontWeights.Length)
        {
            weightIndex = 0;
        }

        // 先不进行内置字体特殊处理。
        var fontFamily = new FontFamily(fontFamilyName);
        Current.Resources["AppFontFamily"] = fontFamily;
        Current.Resources["AppFontWeight"] = UiFontWeights[weightIndex];
        Current.Resources["ContentControlThemeFontFamily"] = fontFamily;
    }

    private static string ResolveUiFontFamilyName(string? uiFontFamilyName, int uiFontFamilyIndex)
    {
        if (!string.IsNullOrWhiteSpace(uiFontFamilyName))
        {
            return uiFontFamilyName;
        }

        if (uiFontFamilyIndex < 0 || uiFontFamilyIndex >= LegacyUiFontFamilies.Length)
        {
            return LegacyUiFontFamilies[0];
        }

        return LegacyUiFontFamilies[uiFontFamilyIndex];
    }
    
    #endregion

    #region TrayIcon

    private void CreateTrayIconMenu()
    {
        var menu = (this.FindResource("AppMenu") as NativeMenu)!;
        menu.Items.Clear();

        var menuAbout = new NativeMenuItem
        {
            Header = "SecRandom",
            Icon = OnPlatformExtension.ShouldProvideOption("OSX")
                ? null
                : new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom/Assets/AppLogo.png"))),
        };
        menuAbout.Click += (sender, e) =>
        {
            ShowSettingsWindow();
            IAppHost.GetService<SettingsView>().SelectNavigationItemById("settings.about");
        };
        menu.Items.Add(menuAbout);

        menu.Items.Add(new NativeMenuItemSeparator());
        
        var menuOpenMainWindow = new NativeMenuItem(Langs.Common.Resources.OpenMainWindow);
        menuOpenMainWindow.Click += (sender, args) =>
        {
            ShowMainWindow();
        };
        menu.Items.Add(menuOpenMainWindow);
        
        var menuOpenSettings = new NativeMenuItem(Langs.Common.Resources.OpenSettings);
        menuOpenSettings.Click += (sender, args) =>
        {
            ShowSettingsWindow();
        };
        menu.Items.Add(menuOpenSettings);
        
        menu.Items.Add(new NativeMenuItemSeparator());
        
        var menuExitProgram = new NativeMenuItem(Langs.Common.Resources.ExitProgram);
        menuExitProgram.Click += (sender, args) =>
        {
            Stop();
        };
        menu.Items.Add(menuExitProgram);
    }

    #endregion
}
