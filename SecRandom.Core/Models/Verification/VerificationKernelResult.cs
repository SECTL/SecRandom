namespace SecRandom.Core.Models.Verification;

public readonly record struct VerificationWinner(Guid RecordId, uint OccurrenceIndex);

public sealed class VerificationKernelResult
{
    public IReadOnlyList<VerificationWinner> Winners { get; init; } = [];
}
