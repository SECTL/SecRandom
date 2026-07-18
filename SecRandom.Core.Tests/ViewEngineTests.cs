using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Views;

namespace SecRandom.Core.Tests;

public sealed class ViewEngineTests
{
    [Fact]
    public void RegistryRejectsDuplicateIdsAndNonControlViews()
    {
        var registry = new ViewRegistry();
        registry.Register(new ViewDescriptor { Id = "test.view", ViewType = typeof(TestView) });

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new ViewDescriptor { Id = "test.view", ViewType = typeof(TestView) }));
        Assert.Throws<ArgumentException>(() => registry.Register(
            new ViewDescriptor { Id = "test.invalid", ViewType = typeof(object) }));
    }

    [Fact]
    public void PluginDescriptorRequiresOwnedPrefix()
    {
        var descriptor = new ViewDescriptor
        {
            Id = "main.rollCall",
            PluginId = "demo",
            ViewType = typeof(TestView)
        };

        Assert.Throws<ArgumentException>(descriptor.Validate);
    }

    [Fact]
    public async Task CloseGuardCanRejectUserCloseButNotHostShutdown()
    {
        var host = new TestHost(ViewPresentationPreference.Default);
        using var provider = CreateProvider(host, out var registry);
        registry.Register(new ViewDescriptor { Id = "test.view", ViewType = typeof(TestView) });
        var service = provider.GetRequiredService<IViewService>();

        var session = await service.ShowAsync("test.view");
        session.Closing += (_, args) => args.Cancel = true;

        Assert.False(await service.CloseAsync(session, ViewCloseReason.User));
        Assert.False(session.IsClosed);
        Assert.True(await service.CloseAsync(session, ViewCloseReason.HostShutdown, isCancelable: false));
        Assert.True(session.IsClosed);
        Assert.Equal(1, host.CloseCount);
    }

    [Fact]
    public async Task ClosingModalCompletesWithTheAssignedResult()
    {
        var host = new TestHost(ViewPresentationPreference.Modal);
        using var provider = CreateProvider(host, out var registry);
        registry.Register(new ViewDescriptor
        {
            Id = "test.modal",
            ViewType = typeof(TestView),
            DefaultPresentation = ViewPresentationPreference.Modal
        });
        var service = provider.GetRequiredService<IViewService>();

        var session = await service.ShowAsync("test.modal");
        session.SetResult("accepted");

        Assert.True(await service.CloseAsync(session));
        Assert.Equal("accepted", await session.Completion);
        Assert.Equal(1, host.ShowCount);
        Assert.Equal(1, host.CloseCount);
    }

    [Fact]
    public async Task UnsupportedPresentationFailsBeforeCreatingAView()
    {
        var host = new TestHost(ViewPresentationPreference.Default);
        using var provider = CreateProvider(host, out var registry);
        registry.Register(new ViewDescriptor { Id = "test.view", ViewType = typeof(TestView) });
        var service = provider.GetRequiredService<IViewService>();

        await Assert.ThrowsAsync<ViewPresentationNotSupportedException>(() => service.ShowAsync(
            "test.view",
            new ViewShowOptions { Presentation = ViewPresentationPreference.Sheet }));
        Assert.Equal(0, host.ShowCount);
    }

    private static ServiceProvider CreateProvider(TestHost host, out IViewRegistry registry)
    {
        var services = new ServiceCollection();
        services.AddViewEngine();
        services.AddSingleton<IViewHostRegistry>(new TestHostRegistry(host));
        var provider = services.BuildServiceProvider();
        registry = provider.GetRequiredService<IViewRegistry>();
        return provider;
    }

    private sealed class TestView : UserControl
    {
    }

    private sealed class TestHost(params ViewPresentationPreference[] supported) : IViewHost
    {
        public int ShowCount { get; private set; }
        public int CloseCount { get; private set; }

        public bool Supports(ViewPresentationPreference presentation) => supported.Contains(presentation);

        public Task ShowAsync(ViewSession session, CancellationToken cancellationToken = default)
        {
            ShowCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync(ViewSession session, CancellationToken cancellationToken = default)
        {
            CloseCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestHostRegistry(TestHost host) : IViewHostRegistry
    {
        public IViewHost Resolve(ViewPresentationPreference presentation) => host;
    }
}
