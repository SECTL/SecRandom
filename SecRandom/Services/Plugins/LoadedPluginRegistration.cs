using SecRandom.Core.Plugins;

namespace SecRandom.Services.Plugins;

public sealed class LoadedPluginRegistration(
    ISecRandomPlugin plugin,
    IPluginRuntimeContext runtimeContext)
{
    public ISecRandomPlugin Plugin { get; } = plugin;
    public IPluginRuntimeContext RuntimeContext { get; } = runtimeContext;
}
