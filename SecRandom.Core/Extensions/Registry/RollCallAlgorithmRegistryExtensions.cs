using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Draw;

namespace SecRandom.Core.Extensions.Registry;

public static class RollCallAlgorithmRegistryExtensions
{
    public static IServiceCollection AddRollCallAlgorithm<T>(this IServiceCollection services, string id, string name)
        where T : class, IRollCallAlgorithm
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("点名算法 Id 不能为空。", nameof(id));
        if (RollCallAlgorithmRegistryService.RegisteredAlgorithms.Any(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"点名算法 Id {id} 已经被占用。", nameof(id));
        services.AddTransient<IRollCallAlgorithm, T>();
        RollCallAlgorithmRegistryService.RegisteredAlgorithms.Add(new(typeof(T), id.Trim(), name));
        return services;
    }
}
