namespace SecRandom.Core.Models.Verification;

/// <summary>
///     One physical entry in a frozen draw pool. OccurrenceIndex distinguishes repeated prize inventory.
/// </summary>
public readonly record struct VerificationCandidate(
    Guid RecordId,
    uint OccurrenceIndex,
    long WeightMicros,
    bool IsGuaranteed);
