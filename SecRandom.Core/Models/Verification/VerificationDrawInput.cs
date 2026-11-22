using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Models.Verification;

public sealed class VerificationDrawInput
{
    public required VerificationDrawKind Kind { get; init; }
    public VerificationSamplingMode SamplingMode { get; init; } = VerificationSamplingMode.HistoryBalancedWeighted;
    public VerificationAlgorithmProfile AlgorithmProfile { get; init; } = VerificationAlgorithmProfile.StudentFairNoRepeat;
    public required int Count { get; init; }
    public IReadOnlyList<VerificationCandidate> Candidates { get; init; } = [];
    public byte[] AuditPayload { get; init; } = [];

    /// <summary>
    ///     The draw mode as it appears in the audit payload (<c>repeat</c>, <c>no-repeat</c>, <c>half-repeat</c>,
    ///     <c>orchestrated</c>). Carried on the input so the proof context projects the same token instead of
    ///     re-deriving it from the algorithm profile.
    /// </summary>
    public string DrawModeToken { get; init; } = string.Empty;

    /// <summary>
    ///     Which history slice the audit's per-candidate figures were read from. Only the axis name is ever
    ///     emitted, never the course or the record group behind it.
    /// </summary>
    public string HistoryScope { get; init; } = "global";

    // Arranged draws only; both null while the draw sampled a pool.
    public int? OrchestrationRound { get; init; }
    public int? OrchestrationRoundCount { get; init; }

    /// <summary>
    ///     Legacy proof-context slot retained so older v2 context hashes remain readable. New draws never apply
    ///     hidden candidate rules and therefore leave this value at zero.
    /// </summary>
    public int ExcludedCandidateCount { get; init; }
}
