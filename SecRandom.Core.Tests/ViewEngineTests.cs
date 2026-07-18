using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Views;

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

    private sealed class TestViewState
    {
        public TestView? LastCreated { get; set; }
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

        public Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default) => Task.CompletedTask;

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
            RaiseDestroyed();
            return Task.CompletedTask;
        }

        public void RaiseDestroyed() => Destroyed?.Invoke(this, EventArgs.Empty);
    }
}
