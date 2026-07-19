using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.HistoryQuery;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCoreRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ConfigServiceBase, FileConfigService>();
        services.AddSingleton<MainConfigHandler>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IDrawTemporaryRecordService, DrawTemporaryRecordService>();
        services.AddSingleton<IHistoryQueryService, HistoryQueryService>();
        services.AddTransient<DrawEngine>();
        services.AddSingleton<IFeatureAvailabilityService, FeatureAvailabilityService>();
        return services;
    }
}
