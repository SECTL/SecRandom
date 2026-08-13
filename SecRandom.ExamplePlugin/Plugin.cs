using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.PluginSdk;
using YamlDotNet.Serialization;

namespace SecRandom.ExamplePlugin;

public sealed class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        _ = new DeserializerBuilder().Build().Deserialize<Dictionary<string, string>>("example: plugin");
    }
}
