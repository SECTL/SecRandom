using Microsoft.Extensions.Logging;
using SecRandom.Core.Plugins;

namespace SecRandom.Services.Plugins;

public sealed class PluginRuntimeContext(
    PluginManifest manifest,
    PluginInfo pluginInfo,
    ILogger logger,
    IPluginDrawInvoker? drawInvoker,
    string dataDirectory) : IPluginRuntimeContext
{
    public PluginManifest Manifest { get; } = manifest;
    public PluginInfo PluginInfo { get; } = pluginInfo;
    public ILogger Logger { get; } = logger;
    public IPluginDrawInvoker? DrawInvoker { get; } = drawInvoker;
    public string DataDirectory { get; } = dataDirectory;
}
