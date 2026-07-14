using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     A self-contained, privacy-preserving draw record. Candidate display data deliberately stays outside this contract.
/// </summary>
public sealed record class DrawProof
{
    [JsonPropertyName("format")]
    public string Format { get; init; } = "secrandom-draw-proof/v1";

    [JsonPropertyName("proofId")]
    public Guid ProofId { get; init; } = Guid.NewGuid();

    [JsonPropertyName("parentProofId")]
    public Guid? ParentProofId { get; init; }

    [JsonPropertyName("mode")]
    public VerificationProofMode Mode { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("algorithmId")]
    public string AlgorithmId { get; init; } = string.Empty;

    [JsonPropertyName("algorithmEngineVersion")]
    public string AlgorithmEngineVersion { get; init; } = string.Empty;

    // Keeps deserializing v1 proof files while new exports use algorithmEngineVersion.
    [JsonPropertyName("kernelVersion")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyKernelVersion { get; init; }

    [JsonPropertyName("inputHash")]
    public string InputHash { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public string Payload { get; init; } = string.Empty;

    // JSON bytes encoded as Base64Url. It contains anonymous candidate/history evidence only.
    [JsonPropertyName("auditPayload")]
    public string AuditPayload { get; init; } = string.Empty;

    [JsonPropertyName("result")]
    public DrawProofResult Result { get; init; } = new();

    [JsonPropertyName("witness")]
    public DrawProofWitness? Witness { get; init; }
}

public sealed class DrawProofResult
{
    [JsonPropertyName("winnerRecordIds")]
    public IReadOnlyList<Guid> WinnerRecordIds { get; init; } = [];
}

public sealed class DrawProofWitness
{
    [JsonPropertyName("challenge")]
    public string Challenge { get; init; } = string.Empty;

    [JsonPropertyName("receipt")]
    public string Receipt { get; init; } = string.Empty;

    [JsonPropertyName("keyId")]
    public string KeyId { get; init; } = string.Empty;
}
