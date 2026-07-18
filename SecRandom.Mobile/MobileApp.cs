using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

public sealed class MobileApp : Avalonia.Application
{
    private IHost? _host;
    private bool _stopping;

    public override void Initialize()
    {
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime singleView)
            throw new PlatformNotSupportedException("SecRandom.Mobile requires an Avalonia single-view lifetime.");

        _host = Host
            .CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddPlatformServices(PlatformStartupContext.Current);
                services.AddHttpClient<MobileUpdateService>();
            })
            .Build();
        var capabilities = _host.Services.GetRequiredService<PlatformCapabilities>();
        singleView.MainView = new MobileRootView(capabilities, _host.Services.GetRequiredService<MobileUpdateService>());

        if (singleView is IControlledApplicationLifetime controlled)
            controlled.Exit += (_, _) => _ = StopHostAsync();

        _ = _host.StartAsync();
        base.OnFrameworkInitializationCompleted();
    }

    private async Task StopHostAsync()
    {
        if (_stopping || _host is null)
            return;

        _stopping = true;
        try
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        finally
        {
            _host.Dispose();
            _host = null;
        }
    }
}

public sealed class MobileRootView : UserControl
{
    public MobileRootView(PlatformCapabilities capabilities, MobileUpdateService updateService)
    {
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
            updateStatus.Text = updateService.Status;
            installUpdate.IsVisible = updateService.IsUpdateAvailable;
            checkUpdates.IsEnabled = !updateService.IsBusy;
            installUpdate.IsEnabled = !updateService.IsBusy;
        }
        updateService.PropertyChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(RefreshUpdateUi);
        checkUpdates.Click += async (_, _) => await updateService.CheckAsync();
        installUpdate.Click += async (_, _) => await updateService.DownloadAndInstallAsync();
        RefreshUpdateUi();

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
