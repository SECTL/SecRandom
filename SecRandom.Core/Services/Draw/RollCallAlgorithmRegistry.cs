using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Core.Services.Draw;

public sealed record RollCallAlgorithmRegistration(Type AlgorithmType, string Id, string Name);

public static class RollCallAlgorithmRegistryService
{
    public static IList<RollCallAlgorithmRegistration> RegisteredAlgorithms { get; } = [];
}

public sealed record LotteryAlgorithmRegistration(Type AlgorithmType, string Id, string Name);

public static class LotteryAlgorithmRegistryService
{
    public static IList<LotteryAlgorithmRegistration> RegisteredAlgorithms { get; } = [];
}

internal sealed class LotteryAlgorithmRegistry(IEnumerable<ILotteryAlgorithm> algorithms) : ILotteryAlgorithmRegistry
{
    private readonly IReadOnlyDictionary<string, ILotteryAlgorithm> _algorithms = BuildMap(algorithms);

    private static IReadOnlyDictionary<string, ILotteryAlgorithm> BuildMap(IEnumerable<ILotteryAlgorithm> algorithms)
    {
        var instances = algorithms.ToArray();
        if (instances.Length == 0)
            instances = [new InventoryLotteryAlgorithm(), new WeightedLotteryAlgorithm()];
        var registrations = LotteryAlgorithmRegistryService.RegisteredAlgorithms;
        var mapped = instances.Zip(registrations)
            .ToDictionary(pair => pair.Second.Id, pair => pair.First, StringComparer.OrdinalIgnoreCase);
        if (mapped.Count > 0)
            return mapped;

        return instances.ToDictionary(
            algorithm => algorithm switch
            {
                InventoryLotteryAlgorithm => "builtin.inventory",
                WeightedLotteryAlgorithm => "builtin.weighted",
                _ => algorithm.GetType().FullName ?? algorithm.GetType().Name
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public ILotteryAlgorithm Resolve(string? id) =>
        !string.IsNullOrWhiteSpace(id) && _algorithms.TryGetValue(id, out var algorithm)
            ? algorithm
            : _algorithms.TryGetValue("builtin.inventory", out var fallback) ? fallback : _algorithms.Values.First();

    public IReadOnlyList<ILotteryAlgorithm> GetAll() => _algorithms.Values.ToArray();
}

internal sealed class RollCallAlgorithmRegistry(IEnumerable<IRollCallAlgorithm> algorithms) : IRollCallAlgorithmRegistry
{
    private readonly IReadOnlyDictionary<string, IRollCallAlgorithm> _algorithms = BuildMap(algorithms);

    private static IReadOnlyDictionary<string, IRollCallAlgorithm> BuildMap(IEnumerable<IRollCallAlgorithm> algorithms)
    {
        var instances = algorithms.ToArray();
        if (instances.Length == 0)
            instances = [new FairRollCallAlgorithm(), new RandomRollCallAlgorithm()];
        var registrations = RollCallAlgorithmRegistryService.RegisteredAlgorithms;
        var mapped = instances.Zip(registrations)
            .ToDictionary(pair => pair.Second.Id, pair => pair.First, StringComparer.OrdinalIgnoreCase);
        if (mapped.Count > 0)
            return mapped;

        return instances.ToDictionary(
            algorithm => algorithm switch
            {
                FairRollCallAlgorithm => "builtin.fair",
                RandomRollCallAlgorithm => "builtin.random",
                _ => algorithm.GetType().FullName ?? algorithm.GetType().Name
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public IRollCallAlgorithm Resolve(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && _algorithms.TryGetValue(id, out var algorithm))
            return algorithm;

        return _algorithms.TryGetValue("builtin.fair", out var fallback)
            ? fallback
            : _algorithms.Values.First();
    }

    public IReadOnlyList<IRollCallAlgorithm> GetAll() => _algorithms.Values.ToArray();
}
