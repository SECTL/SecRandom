using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services;
using SecRandom.Mobile;
using SecRandom.Services.Draw;
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
        services.AddTransient<RollCallDrawService>();
        services.AddTransient<LotteryDrawService>();
        services.AddSingleton<IMobileMediaPlayer, UnsupportedMobileMediaPlayer>();
        services.AddSingleton<MobileMediaLibraryService>();
        services.AddSingleton<MobileDrawMediaService>();
        services.AddSingleton<IMobileCapabilities, TestCapabilities>();
        services.AddSingleton<IMobileSettingsNavigator, TestSettingsNavigator>();
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
