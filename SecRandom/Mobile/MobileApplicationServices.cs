using System.Globalization;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Attributes;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Icons;
using SecRandom.Core.Models;
using SecRandom.Core.Views;
using SecRandom.Services.Mobile;
using SecRandom.Views.Mobile;
using SecRandom.Views.Mobile.Settings;
using SecRandom.Platforms.Abstractions;
using LR = SecRandom.Langs.Mobile.Resources;

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
        services.AddSingleton<IMobileCapabilities, MobileCapabilities>();
        services.AddSingleton<IMobileNavigator, MobileNavigator>();
        services.AddSingleton<IMobileSettingsNavigator, MobileSettingsNavigator>();
        services.AddSingleton<SingleViewHostProvider>();
        services.AddSingleton<IViewHostProvider>(provider => provider.GetRequiredService<SingleViewHostProvider>());
        services.AddViewEngine()
            .AddView<global::SecRandom.Views.SettingsView>(MobilePageIds.Settings);
        services.AddKeyedTransient<UserControl, MobileDrawPage>(MobilePageIds.Draw);
        services.AddKeyedTransient<UserControl, MobileHistoryPage>(MobilePageIds.History);
        services.AddKeyedTransient<UserControl, MobileOverviewPage>(MobilePageIds.Overview);
        services.AddSettingsPage<MobileSettingsCatalogPage>(LR.P_Settings);
        services.AddGroup(new PageGroupInfo(LR.S_GroupPreferences, "settings.mobile.preferences", FluentIcons.ColorFilled));
        services.AddGroup(new PageGroupInfo(LR.S_GroupData, "settings.mobile.data", FluentIcons.DatabaseFilled));
        services.AddGroup(new PageGroupInfo(LR.S_GroupApp, "settings.mobile.application", FluentIcons.InfoFilled));
        services.AddSettingsPage<MobileGeneralSettingsPage>(LR.S_General);
        services.AddSettingsPage<MobilePersonalizationSettingsPage>(LR.S_Personalization);
        services.AddSettingsPage<MobileDrawSettingsPage>(LR.S_DrawSettings);
        services.AddSettingsPage<MobileListManagementSettingsPage>(LR.S_ListManagement);
        services.AddSettingsPage<MobileBackupSettingsPage>(LR.S_Backup);
        if (platform.UpdateInstaller.IsSupported)
            services.AddSettingsPage<MobileUpdateSettingsPage>(LR.S_AppUpdates);
        services.AddSettingsPage<MobileAboutSettingsPage>(LR.S_About);
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
