using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Views;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Mobile;
using SecRandom.Views;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileSettingsViewTests
{
    private const string ChildPageId = "settings.mobile.test";
    private static readonly SemaphoreSlim HostGate = new(1, 1);

    [AvaloniaFact]
    public async Task BackReturnsToCatalogBeforeClosingSettingsView()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        ViewHostControl? viewHost = null;
        try
        {
            provider = CreateProvider();
            IAppHost.Host = new TestHost(provider);
            viewHost = new ViewHostControl("mobile.settings.test");
            provider.GetRequiredService<SingleViewHostProvider>().Attach(viewHost);
            var navigator = provider.GetRequiredService<IMobileSettingsNavigator>();

            await navigator.OpenAsync(ChildPageId);

            var settings = Assert.IsType<SettingsView>(Assert.Single(viewHost.PageStack));
            var pageHost = settings.FindControl<ContentControl>("MobilePageHost")!;
            var back = settings.FindControl<Button>("MobileBackButton")!;
            Assert.IsType<TestChildPage>(pageHost.Content);
            Assert.True(back.IsVisible);

            Assert.False(await viewHost.CloseActiveViewAsync());
            Assert.IsType<TestCatalogPage>(pageHost.Content);
            Assert.False(back.IsVisible);
            Assert.True(navigator.IsOpen);

            Assert.True(await viewHost.CloseActiveViewAsync());
            Assert.False(navigator.IsOpen);
            Assert.Empty(viewHost.PageStack);
        }
        finally
        {
            if (viewHost is not null)
                await viewHost.DestroyAsync();
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    [AvaloniaFact]
    public async Task HomeClosesSettingsViewFromAChildPage()
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        ViewHostControl? viewHost = null;
        try
        {
            provider = CreateProvider();
            IAppHost.Host = new TestHost(provider);
            viewHost = new ViewHostControl("mobile.settings.home.test");
            provider.GetRequiredService<SingleViewHostProvider>().Attach(viewHost);
            var navigator = provider.GetRequiredService<IMobileSettingsNavigator>();

            await navigator.OpenAsync(ChildPageId);
            var settings = Assert.IsType<SettingsView>(Assert.Single(viewHost.PageStack));
            var completion = new TaskCompletionSource<ViewCloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, args) => completion.TrySetResult(args.CloseResult.Reason);

            settings.FindControl<Button>("MobileHomeButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(ViewCloseReason.User, await completion.Task.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.False(navigator.IsOpen);
            Assert.Empty(viewHost.PageStack);
        }
        finally
        {
            if (viewHost is not null)
                await viewHost.DestroyAsync();
            IAppHost.Host = null;
            provider?.Dispose();
            HostGate.Release();
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        var platform = new MobilePlatformServiceRoot(PlatformKind.Android);
        services.AddSingleton<IPlatformServiceRoot>(platform);
        services.AddSingleton<IMobileSettingsNavigator, MobileSettingsNavigator>();
        services.AddSingleton<SingleViewHostProvider>();
        services.AddSingleton<IViewHostProvider>(provider => provider.GetRequiredService<SingleViewHostProvider>());
        services.AddViewEngine().AddView<SettingsView>(MobilePageIds.Settings);
        services.AddKeyedTransient<UserControl, TestCatalogPage>(MobilePageIds.Settings);
        services.AddKeyedTransient<UserControl, TestChildPage>(ChildPageId);
        return services.BuildServiceProvider();
    }

    private sealed class TestCatalogPage : UserControl;

    private sealed class TestChildPage : UserControl;

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Dispose()
        {
        }
    }
}
