using System.Text.Json.Serialization;

namespace SecRandom.Shared.Models.Verification;

/// <summary>
///     A self-contained, privacy-preserving draw record. Candidate display data deliberately stays outside this contract.
/// </summary>
public sealed record class DrawProof
{
    /// <summary>
    ///     The original proof document: replay evidence only, no readable draw context.
    /// </summary>
    public const string FormatV1 = "secrandom-draw-proof/v1";

    /// <summary>
    ///     Adds <see cref="Context" /> and commits it in the attested proof hash, so a receipt covers the draw's
    ///     channel, pool composition, and filters instead of leaving them outside the file.
    /// </summary>
    public const string FormatV2 = "secrandom-draw-proof/v2";

    [JsonPropertyName("format")]
    public string Format { get; init; } = FormatV2;

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

    // Readable draw context. Absent in a v1 proof, and ignored (but hashed) in a v2 proof that has none.
    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofContext? Context { get; init; }

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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Challenge { get; init; }

    [JsonPropertyName("receipt")]
    public string? Receipt { get; init; }

    // RFC 3161 time-stamp token over the canonical proof hash, requested from an independent authority.
    [JsonPropertyName("timestampToken")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TimestampToken { get; init; }

    [JsonPropertyName("keyId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeyId { get; init; }
}
