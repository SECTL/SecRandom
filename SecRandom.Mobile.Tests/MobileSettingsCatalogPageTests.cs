using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models;
using SecRandom.Core.Services;
using SecRandom.Mobile;
using SecRandom.Services.Mobile;
using SecRandom.Views.Mobile;
using SecRandom.Views.Mobile.Settings;

namespace SecRandom.Mobile.Tests;

public sealed class MobileSettingsCatalogPageTests
{
    [AvaloniaFact]
    public void CatalogLoadsAndRoutesRegisteredDesktopPages()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMobileSettingsNavigator, TestSettingsNavigator>();
        using var provider = services.BuildServiceProvider();
        IAppHost.Host = new TestHost(provider);
        try
        {
            PagesRegistryService.SettingsItems.Clear();
            PagesRegistryService.GroupItems.Clear();
            PagesRegistryService.SettingsItems.Add(new PageInfo("settings.general.basic", FluentIcons.SettingsFilled,
                groupId: "settings.general") { Name = "Basic" });
            PagesRegistryService.GroupItems.Add(new PageGroupInfo("General", "settings.general", FluentIcons.SettingsFilled));

            var page = new MobileSettingsCatalogPage(provider.GetRequiredService<IMobileSettingsNavigator>());

            Assert.NotNull(page.FindControl<ScrollViewer>("PageScroll"));
            Assert.Single(page.Items);
            Assert.Equal("settings.general.basic", page.Items[0].Pages[0].Id);
        }
        finally
        {
            PagesRegistryService.SettingsItems.Clear();
            PagesRegistryService.GroupItems.Clear();
            IAppHost.Host = null;
        }
    }

    private sealed class TestSettingsNavigator : IMobileSettingsNavigator
    {
        public bool IsOpen => false;
        public Task OpenAsync(string? pageId = null) => Task.CompletedTask;
        public Task NavigateAsync(string pageId) => Task.CompletedTask;
        public Task CloseAsync() => Task.CompletedTask;
    }

    private sealed class TestHost(IServiceProvider services) : IHost
    {
        public IServiceProvider Services { get; } = services;
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
    }
}
