using Microsoft.Extensions.Logging;

namespace SecRandom.Core.Plugins;

public interface ISecRandomPlugin
{
    string Id { get; }
    void ConfigureServices(IPluginServiceCollection services, IPluginBuildContext context);
    Task OnLoadedAsync(IPluginRuntimeContext context);
}

public interface IPluginServiceCollection
{
    void AddMainPage(PluginPageRegistration registration);
    void AddSettingsPage(PluginPageRegistration registration);
    void AddView(PluginViewRegistration registration) =>
        throw new NotSupportedException("The current plugin service collection does not support views.");
}

public interface IPluginBuildContext
{
    PluginManifest Manifest { get; }
    PluginInfo PluginInfo { get; }
}

public interface IPluginRuntimeContext
{
    PluginManifest Manifest { get; }
    PluginInfo PluginInfo { get; }
    ILogger Logger { get; }
    IPluginDrawInvoker? DrawInvoker { get; }
    string DataDirectory { get; }
    IPluginViewService? ViewService => null;
}

public sealed class PluginInfo
{
    public required PluginManifest Manifest { get; init; }
    public required string PluginDirectory { get; init; }
    public required string ConfigDirectory { get; init; }
}
