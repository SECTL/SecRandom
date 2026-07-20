using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Views;
using SecRandom.Services.ViewEngine;

namespace SecRandom.Core.Tests;

public sealed class ViewEngineTests
{
    [Fact]
    public void RegistryRejectsDuplicateAndNonViewBaseRegistrations()
    {
        var registry = new ViewRegistry();
        registry.Register(new ViewRegistration { Id = "test.view", ViewType = typeof(TestView) });

        Assert.Throws<InvalidOperationException>(() => registry.Register(
            new ViewRegistration { Id = "test.view", ViewType = typeof(TestView) }));
        Assert.Throws<ArgumentException>(() => registry.Register(
            new ViewRegistration { Id = "test.invalid", ViewType = typeof(object) }));
    }

    [Fact]
    public void ViewRegistrationRequiresTheReservedPluginPrefix()
    {
        Assert.Throws<ArgumentException>(() => new ViewRegistration
        {
            Id = "main.rollCall",
            PluginId = "demo",
            ViewType = typeof(TestView)
        }.Validate());
    }

    [Fact]
    public async Task ClosingEventCanCancelUserCloseButNotHostDestruction()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new TestHost("test-host");
        using var provider = CreateProvider(host, out var state);
        var engine = provider.GetRequiredService<IViewEngine>();

        var handle = await engine.ShowAsync("test.view", cancellationToken: cancellationToken);
        var view = Assert.IsType<TestView>(state.LastCreated);
        view.Closing += (_, args) => args.Cancel = true;

        var canceled = await view.CloseAsync(reason: ViewCloseReason.User, cancellationToken: cancellationToken);
        Assert.True(canceled.WasCanceled);

        host.RaiseDestroyed();
        var closed = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.True(closed.WasClosed);
        Assert.Equal(ViewCloseReason.HostDestroyed, closed.Reason);
        Assert.Equal(1, host.CloseCount);
    }

    [Fact]
    public async Task ModalCompletionCarriesTheCloseResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new TestHost("test-host");
        using var provider = CreateProvider(host, out var state);
        var engine = provider.GetRequiredService<IViewEngine>();

        var modal = engine.ShowModalAsync("test.modal", cancellationToken: cancellationToken);
        await WaitForAsync(() => state.LastCreated is not null, cancellationToken);

        var close = await state.LastCreated!.CloseAsync("accepted", cancellationToken: cancellationToken);
        var result = await modal;

        Assert.True(close.WasClosed);
        Assert.True(result.WasClosed);
        Assert.Equal("accepted", result.Result);
        Assert.Equal(1, host.ModalShowCount);
        Assert.Equal(1, host.CloseCount);
    }

    [Fact]
    public async Task SingleViewProviderRejectsNewHostRequests()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new TestHost("single-host");
        using var provider = CreateProvider(host, out _);
        var engine = provider.GetRequiredService<IViewEngine>();

        var exception = await Assert.ThrowsAsync<ViewHostUnavailableException>(() => engine.ShowAsync(
            "test.view",
            new ViewShowOptions { ActivationPreference = ViewActivationPreference.NewHost },
            cancellationToken));

        Assert.True(exception.Selection.IsUnsupported);
        Assert.Equal(0, host.PageShowCount);
    }

    [Fact]
    public async Task DefaultDesktopLikeProviderCanReuseItsCurrentHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new TestHost("reused-host");
        using var provider = CreateProvider(host, out _);
        var engine = provider.GetRequiredService<IViewEngine>();

        await engine.ShowAsync("test.view", cancellationToken: cancellationToken);
        await engine.ShowAsync("test.modal", cancellationToken: cancellationToken);

        Assert.Equal(1, host.PageShowCount);
        Assert.Equal(1, host.ModalShowCount);
    }

    [Fact]
    public async Task ApplicationShutdownClosesCancelableViews()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var host = new TestHost("shutdown-host");
        using var provider = CreateProvider(host, out var state);
        var engine = provider.GetRequiredService<IViewEngine>();

        var handle = await engine.ShowAsync("test.view", cancellationToken: cancellationToken);
        Assert.IsType<TestView>(state.LastCreated).Closing += (_, args) => args.Cancel = true;

        await engine.CloseHostAsync(host, ViewCloseReason.ApplicationShutdown, cancellationToken);
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);

        Assert.True(result.WasClosed);
        Assert.Equal(ViewCloseReason.ApplicationShutdown, result.Reason);
    }

    [Fact]
    public async Task ShowModalAsync_PreservesHostId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var provider = new RecordingHostProvider();
        var services = new ServiceCollection();
        services.AddSingleton<IViewHostProvider>(provider);
        services.AddViewEngine().AddView<TestView>("test.modal", ViewPresentation.Modal);
        await using var serviceProvider = services.BuildServiceProvider();
        var engine = serviceProvider.GetRequiredService<IViewEngine>();

        await Assert.ThrowsAsync<ViewHostUnavailableException>(() => engine.ShowModalAsync(
            "test.modal",
            new ViewShowOptions { HostId = "desktop.main" },
            cancellationToken));

        Assert.Equal("desktop.main", provider.LastOptions?.HostId);
        Assert.Equal(ViewPresentation.Modal, provider.LastOptions?.Presentation);
    }

    [Fact]
    public async Task ShowAsync_ReusingSessionOnDisposedHostDropsStaleSessionAndRecreatesView()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var staleHost = new TestHost("host.stale");
        var replacementHost = new TestHost("host.replacement");
        var services = new ServiceCollection();
        services.AddSingleton<TestViewState>();
        services.AddSingleton<IViewHostProvider>(new SequenceHostProvider(staleHost, replacementHost));
        services.AddViewEngine().AddView<TestView>("test.view");
        await using var provider = services.BuildServiceProvider();
        var engine = provider.GetRequiredService<IViewEngine>();
        var state = provider.GetRequiredService<TestViewState>();

        var firstHandle = await engine.ShowAsync("test.view", cancellationToken: cancellationToken);
        var firstView = Assert.IsType<TestView>(state.LastCreated);

        // 模拟桌面关窗清理竞态：host 已销毁（激活抛 ObjectDisposedException），
        // 但 Destroyed 通知尚未把会话从引擎摘掉。
        staleHost.ThrowOnActivate = true;

        await engine.ShowAsync("test.view", cancellationToken: cancellationToken);

        var staleResult = await firstHandle.Completion.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        Assert.True(staleResult.WasClosed);
        Assert.Equal(ViewCloseReason.HostDestroyed, staleResult.Reason);
        Assert.True(firstView.IsClosed);
        Assert.NotSame(firstView, state.LastCreated);
        Assert.Equal(1, replacementHost.PageShowCount);
    }

    [Fact]
    public async Task DesktopProvider_RoutesNamedPhysicalHost()
    {
        var provider = new DesktopViewHostProvider();
        var host = new TestHost("desktop.main");
        provider.RegisterHost(host);

        var selection = await provider.GetHostAsync(
            new ViewShowOptions { HostId = "desktop.main" },
            TestContext.Current.CancellationToken);

        Assert.True(selection.IsSuccess);
        Assert.Same(host, selection.Host);
    }

    [Fact]
    public async Task DesktopProvider_RejectsUnknownNamedPhysicalHostWithoutCreatingWindow()
    {
        var provider = new DesktopViewHostProvider();

        var selection = await provider.GetHostAsync(
            new ViewShowOptions { HostId = "desktop.unknown" },
            TestContext.Current.CancellationToken);

        Assert.False(selection.IsSuccess);
        Assert.False(selection.IsUnsupported);
        Assert.Null(selection.Host);
    }

    [Fact]
    public async Task DesktopProvider_ReregisteringPhysicalHostUsesReplacement()
    {
        var provider = new DesktopViewHostProvider();
        var stale = new TestHost("desktop.main");
        provider.RegisterHost(stale);

        var replacement = new TestHost("desktop.main");
        provider.RegisterHost(replacement);

        Assert.Equal(0, stale.DestroyCount);
        var selection = await provider.GetHostAsync(
            new ViewShowOptions { HostId = "desktop.main" },
            TestContext.Current.CancellationToken);
        Assert.True(selection.IsSuccess);
        Assert.Same(replacement, selection.Host);
    }

    [Fact]
    public async Task DesktopProvider_UnregistersDestroyedPhysicalHost()
    {
        var provider = new DesktopViewHostProvider();
        var host = new TestHost("desktop.main");
        provider.RegisterHost(host);

        host.RaiseDestroyed();
        var selection = await provider.GetHostAsync(
            new ViewShowOptions { HostId = "desktop.main" },
            TestContext.Current.CancellationToken);

        Assert.False(selection.IsSuccess);
        Assert.Null(selection.Host);
    }

    [Fact]
    public async Task DesktopProvider_RejectsNewHostForNamedPhysicalHost()
    {
        var provider = new DesktopViewHostProvider();
        provider.RegisterHost(new TestHost("desktop.main"));

        var selection = await provider.GetHostAsync(
            new ViewShowOptions
            {
                HostId = "desktop.main",
                ActivationPreference = ViewActivationPreference.NewHost
            },
            TestContext.Current.CancellationToken);

        Assert.False(selection.IsSuccess);
        Assert.True(selection.IsUnsupported);
        Assert.Null(selection.Host);
    }

    [Fact]
    public void ViewHandlesDoNotExposeTheUnderlyingHostOrView()
    {
        var names = typeof(IViewHandle).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Host", names);
        Assert.DoesNotContain("View", names);
    }

    private static ServiceProvider CreateProvider(TestHost host, out TestViewState state)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestViewState>();
        services.AddSingleton<SingleViewHostProvider>();
        services.AddSingleton<IViewHostProvider>(provider => provider.GetRequiredService<SingleViewHostProvider>());
        services.AddViewEngine()
            .AddView<TestView>("test.view")
            .AddView<TestView>("test.modal", ViewPresentation.Modal);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<SingleViewHostProvider>().Attach(host);
        state = provider.GetRequiredService<TestViewState>();
        return provider;
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("The expected view was not created.");

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class TestView : ViewBase
    {
        public TestView(TestViewState state)
        {
            state.LastCreated = this;
        }
    }

    private sealed class RecordingHostProvider : IViewHostProvider
    {
        public ViewShowOptions? LastOptions { get; private set; }

        public Task<ViewHostSelection> GetHostAsync(ViewShowOptions options, CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(ViewHostSelection.Failed("No host available for this test."));
        }
    }

    private sealed class TestViewState
    {
        public TestView? LastCreated { get; set; }
    }

    private sealed class SequenceHostProvider(params IViewHost[] hosts) : IViewHostProvider
    {
        private int _index;

        public Task<ViewHostSelection> GetHostAsync(ViewShowOptions options, CancellationToken cancellationToken = default)
        {
            var host = hosts[Math.Min(_index++, hosts.Length - 1)];
            return Task.FromResult(ViewHostSelection.Success(host));
        }
    }

    private sealed class TestHost(string hostId) : IViewHost
    {
        private readonly List<ViewBase> _pages = [];

        public string HostId { get; } = hostId;
        public IReadOnlyList<ViewBase> PageStack => _pages;
        public ViewBase? ActiveModalView { get; private set; }
        public int PageShowCount { get; private set; }
        public int ModalShowCount { get; private set; }
        public int CloseCount { get; private set; }
        public int ActivateCount { get; private set; }
        public int DestroyCount { get; private set; }
        public bool ThrowOnActivate { get; set; }
        public event EventHandler? Destroyed;

        public Task ShowPageAsync(ViewBase view, CancellationToken cancellationToken = default)
        {
            PageShowCount++;
            _pages.Add(view);
            return Task.CompletedTask;
        }

        public Task ShowModalAsync(ViewBase view, CancellationToken cancellationToken = default)
        {
            ModalShowCount++;
            ActiveModalView = view;
            return Task.CompletedTask;
        }

        public Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default)
        {
            if (ThrowOnActivate)
                throw new ObjectDisposedException(HostId, "The view host has been destroyed.");

            ActivateCount++;
            return Task.CompletedTask;
        }

        public Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default)
        {
            CloseCount++;
            _pages.Remove(view);
            if (ReferenceEquals(ActiveModalView, view))
                ActiveModalView = null;
            return Task.CompletedTask;
        }

        public Task DestroyAsync(CancellationToken cancellationToken = default)
        {
            DestroyCount++;
            RaiseDestroyed();
            return Task.CompletedTask;
        }

        public void RaiseDestroyed() => Destroyed?.Invoke(this, EventArgs.Empty);
    }
}
