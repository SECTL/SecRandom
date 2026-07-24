using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services;
using SecRandom.Mobile;
using SecRandom.Services.Mobile;
using SecRandom.Shared;
using SecRandom.ViewModels.Mobile;
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
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = outlet
        };
        window.Show();
        navigator.Attach(outlet);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Draw));
        Dispatcher.UIThread.RunJobs();
        var drawPage = Assert.IsType<MobileDrawPage>(outlet.Content);
        Assert.NotNull(drawPage.FindControl<TabStrip>("DrawSurfaceTabs"));
        Assert.Equal(0, drawPage.FindControl<Carousel>("DrawSurfaceCarousel")!.SelectedIndex);

        Assert.True(await navigator.NavigateRootAsync(MobileDestination.Overview));
        var overviewPage = Assert.IsType<MobileOverviewPage>(outlet.Content);
        Assert.NotNull(overviewPage.FindControl<TextBlock>("StudentMetricValue"));
        window.Close();
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
        services.AddTransient<MobileRollCallService>();
        services.AddSingleton<IMobileMediaPlayer, UnsupportedMobileMediaPlayer>();
        services.AddSingleton<MobileMediaLibraryService>();
        services.AddSingleton<MobileDrawMediaService>();
        services.AddSingleton<IMobileCapabilities, TestCapabilities>();
        services.AddSingleton<IMobileSettingsNavigator, TestSettingsNavigator>();
        services.AddSingleton<IMobileNavigator, MobileNavigator>();
        services.AddTransient<MobileDrawPageViewModel>();
        services.AddKeyedTransient<UserControl, MobileDrawPage>(MobilePageIds.Draw);
        services.AddKeyedTransient<UserControl, MobileHistoryPage>(MobilePageIds.History);
        services.AddKeyedTransient<UserControl, MobileOverviewPage>(MobilePageIds.Overview);
        return services.BuildServiceProvider();
    }

    private sealed class TestCapabilities : IMobileCapabilities
    {
        public bool IsLotteryEnabled => true;
        public bool SupportsInAppUpdate => false;
    }

    private sealed class TestSettingsNavigator : IMobileSettingsNavigator
    {
        public bool IsOpen => false;

        public Task OpenAsync(string? pageId = null) => Task.CompletedTask;

        public Task NavigateAsync(string pageId) => Task.CompletedTask;

        public Task CloseAsync() => Task.CompletedTask;
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
