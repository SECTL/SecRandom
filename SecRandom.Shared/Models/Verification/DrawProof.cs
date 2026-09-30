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

    // Local append-only evidence chain. Absent on proofs written before the chain existed (and on
    // proofs whose chain could not be advanced); such files are reported as "unchained" instead of broken.
    [JsonPropertyName("chain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofChain? Chain { get; init; }

    [JsonPropertyName("witness")]
    public DrawProofWitness? Witness { get; init; }
}

/// <summary>
///     Position of one proof inside the local append-only chain. Deleting or rewriting a chained proof
///     becomes visible because the surrounding files still link to its hash.
/// </summary>
public sealed class DrawProofChain
{
    [JsonPropertyName("formatVersion")]
    public int FormatVersion { get; init; } = 1;

    [JsonPropertyName("index")]
    public long Index { get; init; }

    [JsonPropertyName("prevHash")]
    public string PrevHash { get; init; } = string.Empty;

    // Hash over (index, prevHash, canonical proof hash). It excludes itself, so it cannot be self-referential.
    [JsonPropertyName("selfHash")]
    public string SelfHash { get; init; } = string.Empty;
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
