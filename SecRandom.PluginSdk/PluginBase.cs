using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SecRandom.PluginSdk;

/// <summary>
/// Entry point for an in-process SecRandom plugin.
/// </summary>
public abstract class PluginBase
{
    public PluginInfo Info { get; internal set; } = null!;

    public string PluginConfigFolder { get; internal set; } = string.Empty;

    /// <summary>
    /// Registers plugin services and Core extensions before the application Host is built.
    /// </summary>
    public abstract void Initialize(HostBuilderContext context, IServiceCollection services);
}
