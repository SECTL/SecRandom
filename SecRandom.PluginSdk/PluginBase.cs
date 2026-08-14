using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SecRandom.PluginSdk;

/// <summary>
/// Entry point for an in-process SecRandom plugin. The host disposes the entrance through
/// <see cref="IAsyncDisposable"/> when the application shuts down. Override
/// <see cref="OnAppStarted"/> / <see cref="OnAppStopping"/> for lifecycle work without registering
/// a separate hosted service; the host forwards the application start/stop events to every entrance.
/// </summary>
public abstract class PluginBase : IAsyncDisposable
{
    public PluginInfo Info { get; internal set; } = null!;

    public string PluginConfigFolder { get; internal set; } = string.Empty;

    /// <summary>
    /// Registers plugin services and Core extensions before the application Host is built.
    /// </summary>
    public abstract void Initialize(HostBuilderContext context, IServiceCollection services);

    /// <summary>
    /// Called after the application finished startup (post window setup). Override to initialize
    /// plugin runtime state that needs the Host and UI ready.
    /// </summary>
    public virtual void OnAppStarted()
    {
    }

    /// <summary>
    /// Called before the application shuts down, before <see cref="DisposeAsync"/>. Override to
    /// stop plugin background work or flush state.
    /// </summary>
    public virtual void OnAppStopping()
    {
    }

    /// <summary>
    /// Releases plugin resources when the host shuts down. Override to clean up timers, IPC, or files.
    /// </summary>
    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
