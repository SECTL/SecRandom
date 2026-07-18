using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.Profiles;
using SecRandom.Core.Views;
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
                    services.AddSingleton<ConfigServiceBase, FileConfigService>();
                    services.AddSingleton<MainConfigHandler>();
                    services.AddSingleton<IProfileService, ProfileService>();
                    services.AddSingleton<IDrawTemporaryRecordService, DrawTemporaryRecordService>();
                    services.AddTransient<DrawEngine>();
                    services.AddSingleton<IFeatureAvailabilityService, FeatureAvailabilityService>();
                    services.AddSingleton<SingleViewHostProvider>();
                    services.AddSingleton<IViewHostProvider>(serviceProvider =>
                        serviceProvider.GetRequiredService<SingleViewHostProvider>());
                    services.AddViewEngine()
                        .AddView<MobileStatusView>(MobileStatusView.Id);
                    services.AddHttpClient<MobileUpdateService>();
                })
                .Build();
            IAppHost.Host = _host;
            _rootView = new MobileRootView();
            _host.Services.GetRequiredService<SingleViewHostProvider>().Attach(_rootView);
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
                await _host.Services.GetRequiredService<IViewEngine>()
                    .CloseHostAsync(_rootView, ViewCloseReason.ApplicationShutdown)
                    .ConfigureAwait(false);
                await _rootView.DestroyAsync().ConfigureAwait(false);
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
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The platform does not provide an application-private data directory.");

        Utils.ConfigureDataRoot(Path.Combine(localApplicationData, "SecRandom", "data"));
    }

    private async Task StartMobileHostAsync(IHost host, ISingleViewApplicationLifetime singleView)
    {
        try
        {
            await host.StartAsync().ConfigureAwait(false);
            if (_stopping || !ReferenceEquals(host, _host))
                return;

            await host.Services.GetRequiredService<IViewEngine>()
                .ShowAsync(MobileStatusView.Id)
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

public sealed class MobileRootView : ViewHostControl
{
    public MobileRootView() : base("mobile.root")
    {
    }
}

public sealed class MobileStatusView : ViewBase
{
    public const string Id = "mobile.status";

    private readonly MobileUpdateService _updateService;
    private readonly System.ComponentModel.PropertyChangedEventHandler _updateStatusChanged;

    public MobileStatusView(PlatformCapabilities capabilities, MobileUpdateService updateService)
    {
        _updateService = updateService;
        var logo = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://SecRandom.Mobile/Assets/AppLogo.png"))),
            Width = 72,
            Height = 72,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = LR.Title,
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var subtitle = new TextBlock
        {
            Text = LR.Subtitle,
            FontSize = 16,
            Opacity = 0.72,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var description = new TextBlock
        {
            Text = LR.Description,
            FontSize = 16,
            LineHeight = 24,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var status = new TextBlock
        {
            Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, LR.PlatformSummary, capabilities.Kind),
            Opacity = 0.68,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        var updateStatus = new TextBlock
        {
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.78
        };
        var installUpdate = new Avalonia.Controls.Button
        {
            Content = LR.InstallUpdate,
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        var checkUpdates = new Avalonia.Controls.Button
        {
            Content = LR.CheckUpdates,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center
        };
        void RefreshUpdateUi()
        {
            updateStatus.Text = _updateService.Status;
            installUpdate.IsVisible = _updateService.IsUpdateAvailable;
            checkUpdates.IsEnabled = !_updateService.IsBusy;
            installUpdate.IsEnabled = !_updateService.IsBusy;
        }
        _updateStatusChanged = (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshUpdateUi);
        _updateService.PropertyChanged += _updateStatusChanged;
        Closed += (_, _) => _updateService.PropertyChanged -= _updateStatusChanged;
        checkUpdates.Click += async (_, _) => await _updateService.CheckAsync();
        installUpdate.Click += async (_, _) => await _updateService.DownloadAndInstallAsync();
        RefreshUpdateUi();
        if (capabilities.Kind == PlatformKind.Android)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = _updateService.CheckAsync());

        Content = new Grid
        {
            Margin = new Thickness(32, 40),
            Children =
            {
                new StackPanel
                {
                    Spacing = 18,
                    MaxWidth = 520,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children = { logo, title, subtitle, description, status, updateStatus, checkUpdates, installUpdate }
                }
            }
        };
    }
}
