using Microsoft.Extensions.DependencyInjection;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms;

public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(
        this IServiceCollection services,
        IPlatformServiceRoot root)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(root);

        services.AddSingleton<IPlatformServiceRoot>(root);
        services.AddSingleton(root.Capabilities);
        services.AddSingleton<IWindowFeatureService>(root.WindowFeatures);
        return services;
    }
}
