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
    private readonly IReadOnlyDictionary<string, ILotteryAlgorithm> _algorithms =
        algorithms.Zip(LotteryAlgorithmRegistryService.RegisteredAlgorithms)
            .ToDictionary(pair => pair.Second.Id, pair => pair.First, StringComparer.OrdinalIgnoreCase);

    public ILotteryAlgorithm Resolve(string? id) =>
        !string.IsNullOrWhiteSpace(id) && _algorithms.TryGetValue(id, out var algorithm)
            ? algorithm
            : _algorithms.TryGetValue("builtin.inventory", out var fallback) ? fallback : _algorithms.Values.First();

    public IReadOnlyList<ILotteryAlgorithm> GetAll() => _algorithms.Values.ToArray();
}

internal sealed class RollCallAlgorithmRegistry(IEnumerable<IRollCallAlgorithm> algorithms) : IRollCallAlgorithmRegistry
{
    private readonly IReadOnlyDictionary<string, IRollCallAlgorithm> _algorithms =
        algorithms.Zip(RollCallAlgorithmRegistryService.RegisteredAlgorithms)
            .ToDictionary(pair => pair.Second.Id, pair => pair.First, StringComparer.OrdinalIgnoreCase);

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
