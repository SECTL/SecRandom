using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Draw;

namespace SecRandom.Core.Extensions.Registry;

public static class LotteryAlgorithmRegistryExtensions
{
    public static IServiceCollection AddLotteryAlgorithm<T>(this IServiceCollection services, string id, string name)
        where T : class, ILotteryAlgorithm
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("抽奖算法 Id 不能为空。", nameof(id));
        if (LotteryAlgorithmRegistryService.RegisteredAlgorithms.Any(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"抽奖算法 Id {id} 已经被占用。", nameof(id));
        services.AddTransient<ILotteryAlgorithm, T>();
        LotteryAlgorithmRegistryService.RegisteredAlgorithms.Add(new(typeof(T), id.Trim(), name));
        return services;
    }
}
