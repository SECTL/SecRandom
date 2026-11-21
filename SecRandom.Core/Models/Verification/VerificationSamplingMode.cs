namespace SecRandom.Core.Models.Verification;

/// <summary>
///     Selects the deterministic sampler committed into a verification request.
/// </summary>
public enum VerificationSamplingMode : byte
{
    HistoryBalancedWeighted = 1,
    InventoryPermutation = 2,
    WeightedWithoutReplacement = 3,

    /// <summary>
    ///     Takes the committed positions instead of sampling. Each drawn candidate carries its 1-based position
    ///     in the round's arranged order in the weight slot (<c>1..count</c>, dense and unique), the replay
    ///     returns exactly those candidates in exactly that order and consumes no random material.
    /// </summary>
    SequentialOrder = 4
}