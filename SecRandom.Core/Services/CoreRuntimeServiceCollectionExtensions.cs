using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Archive;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Services.HistoryQuery;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Services;

public static partial class CoreRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddCoreRuntimeServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ConfigServiceBase, FileConfigService>();
        services.AddSingleton<MainConfigHandler>();
        services.AddSingleton<IProfileService, ProfileService>();
        services.AddSingleton<IProfileCatalogEditor, ProfileCatalogEditor>();
        services.AddSingleton<IProfileCatalogManager, ProfileCatalogManager>();
        services.AddSingleton<IDrawTemporaryRecordService, DrawTemporaryRecordService>();
        services.AddSingleton<IDrawCommitService, DrawCommitCoordinator>();
        services.AddTransient<IRollCallSession, RollCallSession>();
        services.AddTransient<ILotterySession, LotterySession>();
        services.AddSingleton<IHistoryQueryService, HistoryQueryService>();
        services.AddTransient<DrawEngine>();
        services.AddSingleton<IFeatureAvailabilityService, FeatureAvailabilityService>();
        services.AddSingleton<IArchivePostImportHooks, NullArchivePostImportHooks>();
        services.AddSingleton<DataArchiveService>();
        return services;
    }
}
