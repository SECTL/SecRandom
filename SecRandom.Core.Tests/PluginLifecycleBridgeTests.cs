using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction.Services;
using SecRandom.PluginSdk;
using SecRandom.Services.Plugins;

namespace SecRandom.Core.Tests;

public sealed class PluginLifecycleBridgeTests
{
    [Fact]
    public async Task Bridge_ForwardsAppEventsToEveryPluginEntrance()
    {
        var lifecycle = new StubAppLifecycle();
        var first = new RecordingPlugin();
        var second = new RecordingPlugin();
        var bridge = new PluginLifecycleBridge([first, second], lifecycle, NullLogger<PluginLifecycleBridge>.Instance);

        await bridge.StartAsync(CancellationToken.None);
        Assert.Empty(first.Events);
        Assert.Empty(second.Events);

        lifecycle.RaiseStarted();
        Assert.Equal(["Started"], first.Events);
        Assert.Equal(["Started"], second.Events);

        lifecycle.RaiseStopping();
        Assert.Equal(["Started", "Stopping"], first.Events);
        Assert.Equal(["Started", "Stopping"], second.Events);

        await bridge.StopAsync(CancellationToken.None);
        lifecycle.RaiseStarted();
        Assert.Equal(2, first.Events.Count);
        Assert.Equal(2, second.Events.Count);
    }

    [Fact]
    public async Task Bridge_ContinuesWhenOnePluginThrows()
    {
        var lifecycle = new StubAppLifecycle();
        var failing = new ThrowingPlugin();
        var healthy = new RecordingPlugin();
        var bridge = new PluginLifecycleBridge([failing, healthy], lifecycle, NullLogger<PluginLifecycleBridge>.Instance);

        await bridge.StartAsync(CancellationToken.None);
        lifecycle.RaiseStarted();

        Assert.Equal(["Started"], healthy.Events);
    }

    private sealed class StubAppLifecycle : IAppLifecycleService
    {
        public event EventHandler? AppStarted;
        public event EventHandler? AppStopping;

        public void RaiseStarted() => AppStarted?.Invoke(this, EventArgs.Empty);

        public void RaiseStopping() => AppStopping?.Invoke(this, EventArgs.Empty);
    }

    private sealed class RecordingPlugin : PluginBase
    {
        private readonly List<string> _events = [];

        public IReadOnlyList<string> Events => _events;

        public override void Initialize(HostBuilderContext context, IServiceCollection services)
        {
        }

        public override void OnAppStarted()
        {
            _events.Add("Started");
        }

        public override void OnAppStopping()
        {
            _events.Add("Stopping");
        }
    }

    private sealed class ThrowingPlugin : PluginBase
    {
        public override void Initialize(HostBuilderContext context, IServiceCollection services)
        {
        }

        public override void OnAppStarted()
        {
            throw new InvalidOperationException("plugin failure");
        }
    }
}
