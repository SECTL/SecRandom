using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Mobile.Views;

namespace SecRandom.Mobile.Tests;

public sealed class MobileNavigatorTests
{
    [AvaloniaFact]
    public async Task SettingsChildBackReturnsToTheCatalog()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateAsync(MobilePageIds.General));
        Assert.IsType<GeneralPage>(outlet.Content);
        Assert.Equal(MobileDestination.Settings, navigator.CurrentDestination);

        Assert.True(await navigator.GoBackAsync());
        Assert.IsType<SettingsPage>(outlet.Content);
        Assert.False(await navigator.GoBackAsync());
    }

    [AvaloniaFact]
    public async Task RootNavigationClearsSettingsHistory()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateAsync(MobilePageIds.General));
        Assert.True(await navigator.NavigateRootAsync(MobileDestination.History));
        Assert.IsType<HistoryPage>(outlet.Content);
        Assert.Equal(MobileDestination.History, navigator.CurrentDestination);
        Assert.False(await navigator.GoBackAsync());
    }

    [AvaloniaFact]
    public async Task MissingConditionalPageDoesNotChangeTheCurrentPage()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Draw));
        var current = outlet.Content;

        Assert.False(await navigator.NavigateAsync(MobilePageIds.Update));
        Assert.Same(current, outlet.Content);
        Assert.Equal(MobileDestination.Draw, navigator.CurrentDestination);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileNavigator, MobileNavigator>();
        services.AddKeyedTransient<UserControl, DrawPage>(MobilePageIds.Draw);
        services.AddKeyedTransient<UserControl, HistoryPage>(MobilePageIds.History);
        services.AddKeyedTransient<UserControl, OverviewPage>(MobilePageIds.Overview);
        services.AddKeyedTransient<UserControl, SettingsPage>(MobilePageIds.Settings);
        services.AddKeyedTransient<UserControl, GeneralPage>(MobilePageIds.General);
        return services.BuildServiceProvider();
    }

    private sealed class DrawPage : UserControl
    {
    }

    private sealed class HistoryPage : UserControl
    {
    }

    private sealed class OverviewPage : UserControl
    {
    }

    private sealed class SettingsPage : UserControl
    {
    }

    private sealed class GeneralPage : UserControl
    {
    }
}
