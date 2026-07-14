namespace SecRandom.Core.Models.Verification;

/// <summary>
///     Selects the deterministic sampler committed into a verification request.
/// </summary>
public enum VerificationSamplingMode : byte
{
    HistoryBalancedWeighted = 1,
    InventoryPermutation = 2,
    WeightedWithoutReplacement = 3
}
