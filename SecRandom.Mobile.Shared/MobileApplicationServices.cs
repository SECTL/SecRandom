using System.Globalization;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Views;
using SecRandom.Mobile.Services;
using SecRandom.Mobile.Views;
using SecRandom.Mobile.Views.Settings;
using SecRandom.Platforms.Abstractions;
using LR = SecRandom.Mobile.Langs.Mobile.Resources;

namespace SecRandom.Mobile;

public static class MobileApplicationServices
{
    public static IServiceCollection AddMobileRuntimeServices(
        this IServiceCollection services,
        MobilePlatformServiceRoot platform,
        Func<Task> reloadRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(reloadRoot);

        services.AddTransient<MobileRollCallService>();
        services.AddSingleton<MobileMediaLibraryService>();
        services.AddSingleton<IMobileMediaPlayer>(platform.MediaPlayer);
        services.AddSingleton<MobileDrawMediaService>();
        services.AddSingleton<IMobileUpdateInstaller>(platform.UpdateInstaller);
        services.AddHttpClient<MobileUpdateService>();
        services.AddSingleton<MobileDeviceUuidStore>();
        services.AddHostedService<MobileOnlineStatusService>();
        services.AddSingleton<IMobileRootViewReloader>(_ => new MobileRootViewReloader(reloadRoot));
        services.AddSingleton<SingleViewHostProvider>();
        services.AddSingleton<IViewHostProvider>(provider => provider.GetRequiredService<SingleViewHostProvider>());
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
        services.AddTransient<MobileRootView>();
        return services;
    }

    public static void ApplyCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        LR.Culture = culture;
    }

    public static Control CreateStartupFailureView(Exception exception)
    {
        string startupFailedText;
        try
        {
            startupFailedText = LR.M_StartupFailed;
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
                Margin = new Avalonia.Thickness(32),
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };
    }
}

public interface IMobileRootViewReloader
{
    Task ReloadAsync();
}

internal sealed class MobileRootViewReloader(Func<Task> reloadRoot) : IMobileRootViewReloader
{
    public Task ReloadAsync() => reloadRoot();
}
