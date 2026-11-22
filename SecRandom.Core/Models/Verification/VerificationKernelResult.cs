namespace SecRandom.Core.Models.Verification;

public readonly record struct VerificationWinner(Guid RecordId, uint OccurrenceIndex);

public sealed class VerificationKernelResult
{
    public IReadOnlyList<VerificationWinner> Winners { get; init; } = [];
}

/// <summary>
/// Decoded proof payload used by the local proof browser. The payload remains the single wire format shared by
/// drawing, exporting and replay; this wrapper only exposes its already encoded request, seed and response together.
/// </summary>
public sealed record VerificationProofPayload(
    VerificationDrawInput Input,
    byte[] Seed,
    VerificationKernelResult Result);
