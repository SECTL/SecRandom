using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.Core.Extensions.Registry;
using SecRandom.ExamplePlugin.Views.SettingsPages;
using SecRandom.PluginSdk;

namespace SecRandom.ExamplePlugin;

public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSettingsPage<ExampleSettingsPage>("ExamplePlugin");
    }
}
