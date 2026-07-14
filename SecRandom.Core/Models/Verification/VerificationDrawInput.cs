using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Models.Verification;

public sealed class VerificationDrawInput
{
    public required VerificationDrawKind Kind { get; init; }
    public VerificationSamplingMode SamplingMode { get; init; } = VerificationSamplingMode.HistoryBalancedWeighted;
    public required int Count { get; init; }
    public IReadOnlyList<VerificationCandidate> Candidates { get; init; } = [];
    public byte[] AuditPayload { get; init; } = [];
}
