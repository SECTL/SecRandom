using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.Shared;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileDrawPageTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "mobile-draw-tests", Guid.NewGuid().ToString("N"));

    public MobileDrawPageTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    [AvaloniaFact]
    public async Task RootNavigation_InitializesDrawAndOverviewPages()
    {
        using var provider = CreateProvider();
        var navigator = provider.GetRequiredService<IMobileNavigator>();
        var outlet = new ContentControl();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Draw));
        var drawPage = Assert.IsType<MobileDrawPage>(outlet.Content);
        Assert.NotNull(drawPage.FindControl<TabStrip>("DrawSurfaceTabs"));

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Overview));
        var overviewPage = Assert.IsType<MobileOverviewPage>(outlet.Content);
        Assert.NotNull(overviewPage.FindControl<TextBlock>("StudentMetricValue"));
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        services.AddMobileRuntimeServices(new MobilePlatformServiceRoot(PlatformKind.Android), () => Task.CompletedTask);
        return services.BuildServiceProvider();
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
