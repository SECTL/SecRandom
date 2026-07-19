using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using FluentAvalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Views;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Shared;
using SecRandom.Mobile.Views;
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
                        .AddView<MobileShellView>(MobileShellView.Id);
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
                await host.Services.GetRequiredService<IViewEngine>()
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
                .ShowAsync(MobileShellView.Id)
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
