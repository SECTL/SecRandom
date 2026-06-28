using SecRandom.Core.Plugins;

namespace SecRandom.Services.Plugins;

public sealed class PluginBuildContext(PluginManifest manifest, PluginInfo pluginInfo)
    : IPluginBuildContext
{
    public PluginManifest Manifest { get; } = manifest;
    public PluginInfo PluginInfo { get; } = pluginInfo;
}
