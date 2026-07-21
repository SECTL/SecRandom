using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileNavigatorTests
{
    [AvaloniaFact]
    public async Task RootNavigationReplacesTheCurrentPage()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.History));
        Assert.IsType<HistoryPage>(outlet.Content);
        Assert.Equal(MobileDestination.History, navigator.CurrentDestination);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Overview));
        Assert.IsType<OverviewPage>(outlet.Content);
        Assert.Equal(MobileDestination.Overview, navigator.CurrentDestination);
        Assert.False(await navigator.GoBackAsync());
    }

    [AvaloniaFact]
    public async Task SettingsIsNotANormalNavigatorRoute()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Draw));
        var current = outlet.Content;

        Assert.False(await navigator.NavigateRootAsync(MobileDestination.Settings));
        Assert.Same(current, outlet.Content);
        Assert.Equal(MobileDestination.Draw, navigator.CurrentDestination);
    }

    [AvaloniaFact]
    public async Task ResetReturnsToDrawRoot()
    {
        await using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.History));
        await navigator.ResetToDrawAsync();
        Assert.IsType<DrawPage>(outlet.Content);
        Assert.Equal(MobileDestination.Draw, navigator.CurrentDestination);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileNavigator, MobileNavigator>();
        services.AddKeyedTransient<UserControl, DrawPage>(MobilePageIds.Draw);
        services.AddKeyedTransient<UserControl, HistoryPage>(MobilePageIds.History);
        services.AddKeyedTransient<UserControl, OverviewPage>(MobilePageIds.Overview);
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

}
