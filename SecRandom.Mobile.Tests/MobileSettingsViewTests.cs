using System.Reflection;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Services;
using SecRandom.Core.Views;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Mobile;
using SecRandom.ViewModels;
using SecRandom.Views;
using SecRandom.Views.Mobile;

namespace SecRandom.Mobile.Tests;

public sealed class MobileSettingsViewTests
{
    private static readonly SemaphoreSlim HostGate = new(1, 1);

    [AvaloniaFact]
    public async Task SettingsUsesTheSharedDesktopLayout()
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

            await navigator.OpenAsync();

            var settings = Assert.IsType<SettingsView>(Assert.Single(viewHost.PageStack));
            Assert.NotNull(settings.FindControl<FluentAvalonia.UI.Controls.FANavigationView>("NavigationView"));
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
    public async Task SettingsNavigatorClosesTheIndependentView()
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

            await navigator.OpenAsync();
            var settings = Assert.IsType<SettingsView>(Assert.Single(viewHost.PageStack));
            var completion = new TaskCompletionSource<ViewCloseReason>(TaskCreationOptions.RunContinuationsAsynchronously);
            settings.Closed += (_, args) => completion.TrySetResult(args.CloseResult.Reason);

            await navigator.CloseAsync();

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

    [AvaloniaFact]
    public async Task SettingsWindowPreviewRouteFreezesTheRequestedPage()
    {
        await WithSettingsPagesAsync(settings =>
        {
            NavigateSettingsWindowPage("settings.test.first", preview: true);
            Dispatcher.UIThread.RunJobs();

            Assert.True(settings.IsPreviewMode);
            Assert.Equal("settings.test.first", settings.ViewModel.SelectedPageInfo?.Id);
            var frame = settings.FindControl<FAFrame>("NavigationFrame")!;
            var page = Assert.IsType<UserControl>(frame.Content);
            Assert.False(Assert.IsType<Button>(Assert.IsType<ScrollViewer>(page.Content).Content).IsEnabled);
            Assert.True(frame.IsEnabled);
            Assert.True(settings.FindControl<FANavigationView>("NavigationView")!.IsEnabled);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task AuthorizedSettingsWindowRouteRestoresPreviewControls()
    {
        await WithSettingsPagesAsync(settings =>
        {
            settings.NavigateToPreviewPage("settings.test.first");
            Dispatcher.UIThread.RunJobs();
            var frame = settings.FindControl<FAFrame>("NavigationFrame")!;
            var page = Assert.IsType<UserControl>(frame.Content);
            var button = Assert.IsType<Button>(Assert.IsType<ScrollViewer>(page.Content).Content);
            Assert.False(button.IsEnabled);

            NavigateSettingsWindowPage("settings.test.first", preview: false);
            Dispatcher.UIThread.RunJobs();

            Assert.False(settings.IsPreviewMode);
            Assert.True(button.IsEnabled);
            return Task.CompletedTask;
        });
    }

    [AvaloniaFact]
    public async Task PreviewNavigationKeepsTheNewPageFrozen()
    {
        await WithSettingsPagesAsync(settings =>
        {
            settings.NavigateToPreviewPage("settings.test.first");
            Dispatcher.UIThread.RunJobs();
            var frame = settings.FindControl<FAFrame>("NavigationFrame")!;
            var firstPage = Assert.IsType<UserControl>(frame.Content);
            var firstButton = Assert.IsType<Button>(Assert.IsType<ScrollViewer>(firstPage.Content).Content);
            Assert.False(firstButton.IsEnabled);

            settings.SelectNavigationItemById("settings.test.second");
            Dispatcher.UIThread.RunJobs();

            Assert.True(settings.IsPreviewMode);
            Assert.Equal("settings.test.second", settings.ViewModel.SelectedPageInfo?.Id);
            Assert.True(firstButton.IsEnabled);
            var secondPage = Assert.IsType<UserControl>(frame.Content);
            Assert.IsType<Button>(secondPage.Content);
            Assert.False(secondPage.IsEnabled);
            Assert.True(frame.IsEnabled);
            return Task.CompletedTask;
        });
    }

    private static void NavigateSettingsWindowPage(string pageId, bool preview)
    {
        var method = typeof(App).GetMethod("NavigateSettingsWindowPage", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(null, [pageId, preview]);
    }

    private static async Task WithSettingsPagesAsync(Func<SettingsView, Task> test)
    {
        await HostGate.WaitAsync();
        ServiceProvider? provider = null;
        ViewHostControl? viewHost = null;
        var originalPages = PagesRegistryService.SettingsItems.ToArray();
        try
        {
            PagesRegistryService.SettingsItems.Clear();
            PagesRegistryService.SettingsItems.Add(new PageInfo("settings.test.first", FluentIcons.SettingsFilled)
                { Name = "First" });
            PagesRegistryService.SettingsItems.Add(new PageInfo("settings.test.second", FluentIcons.SettingsFilled)
                { Name = "Second" });
            provider = CreateProvider();
            IAppHost.Host = new TestHost(provider);
            viewHost = new ViewHostControl("mobile.settings.preview.test");
            provider.GetRequiredService<SingleViewHostProvider>().Attach(viewHost);
            var navigator = provider.GetRequiredService<IMobileSettingsNavigator>();
            await navigator.OpenAsync();
            var settings = Assert.IsType<SettingsView>(Assert.Single(viewHost.PageStack));

            await test(settings);
        }
        finally
        {
            if (viewHost is not null)
                await viewHost.DestroyAsync();
            IAppHost.Host = null;
            provider?.Dispose();
            PagesRegistryService.SettingsItems.Clear();
            foreach (var page in originalPages)
                PagesRegistryService.SettingsItems.Add(page);
            HostGate.Release();
        }
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<SettingsViewModel>();
        services.AddKeyedSingleton<UserControl>("settings.test.first", new UserControl
        {
            Content = new ScrollViewer { Content = new Button { Content = "First setting" } }
        });
        services.AddKeyedSingleton<UserControl>("settings.test.second", new UserControl
        {
            Content = new Button { Content = "Second setting" }
        });
        var platform = new MobilePlatformServiceRoot(PlatformKind.Android);
        services.AddSingleton<IPlatformServiceRoot>(platform);
        services.AddSingleton<IMobileSettingsNavigator, MobileSettingsNavigator>();
        services.AddSingleton<SingleViewHostProvider>();
        services.AddSingleton<IViewHostProvider>(provider => provider.GetRequiredService<SingleViewHostProvider>());
        services.AddViewEngine().AddView<SettingsView>(MobilePageIds.Settings);
        return services.BuildServiceProvider();
    }

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
