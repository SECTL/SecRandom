using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class WitnessReceipt
{
    public Guid ProofId { get; init; }
    public string InputHash { get; init; } = string.Empty;
    public string PayloadHash { get; init; } = string.Empty;
    public string AuditPayloadHash { get; init; } = string.Empty;
    public string ProofHash { get; init; } = string.Empty;
    public string? LockReceiptHash { get; init; }
    public string? CandidatePoolCommitment { get; init; }
    public string? RosterDigest { get; init; }
    public string? AlgorithmSpecId { get; init; }
    public string? AlgorithmSpecHash { get; init; }
    public string? ClientNonce { get; init; }
    public string? ServerNonce { get; init; }
    public VerificationProofMode Mode { get; init; }
    public string KeyId { get; init; } = string.Empty;
    public DateTimeOffset AttestedAtUtc { get; init; }
    public string? Role { get; init; }
}

public sealed class WitnessAttestationResponse
{
    public string Token { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
}

public sealed record WitnessAttestationResult(string Receipt);

public sealed class FormalNotarizationRequest
{
    public Guid ProofId { get; init; }
    public Guid? ParentProofId { get; init; }
    public string InputHash { get; init; } = string.Empty;
    public string ZeroSeedRequest { get; init; } = string.Empty;
    public string AuditPayload { get; init; } = string.Empty;
    public string ClientNonce { get; init; } = string.Empty;
    public string AlgorithmSpecId { get; init; } = string.Empty;
    public string AlgorithmSpecHash { get; init; } = string.Empty;
    public string AlgorithmEngineVersion { get; init; } = string.Empty;
    public string AlgorithmId { get; init; } = string.Empty;
    public string CandidatePoolCommitment { get; init; } = string.Empty;
    public string RosterDigest { get; init; } = string.Empty;

    // Readable context for the formal proof the service builds. It is part of the request because the notarized
    // document is signed on the server: a block added only after the response could not be covered by that
    // signature. An older service ignores the property and answers with the v1 proof it has always produced.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawProofContext? Context { get; init; }

    // Legacy chain position fields kept on the wire contract only: this build keeps no local proof chain,
    // so they stay null and are omitted from the request. The service never used them for locking.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ChainIndex { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChainHash { get; init; }
}

public sealed class FormalNotarizationResponse
{
    public DrawProof Proof { get; init; } = new();
    public string TicketId { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
}

public sealed class FormalLockReceipt
{
    public Guid ProofId { get; init; }
    public Guid? ParentProofId { get; init; }
    public string TicketId { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string RequestHash { get; init; } = string.Empty;
    public string AuditPayloadHash { get; init; } = string.Empty;
    public string CandidatePoolCommitment { get; init; } = string.Empty;
    public string RosterDigest { get; init; } = string.Empty;
    public string ContextHash { get; init; } = string.Empty;
    public string AlgorithmSpecId { get; init; } = string.Empty;
    public string AlgorithmSpecHash { get; init; } = string.Empty;
    public string AlgorithmEngineVersion { get; init; } = string.Empty;
    public string AlgorithmId { get; init; } = string.Empty;
    public string ClientNonce { get; init; } = string.Empty;
    public string ServerNonce { get; init; } = string.Empty;
    public DateTimeOffset LockedAtUtc { get; init; }
    public string KeyId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public interface IWitnessClient
{
    Task<WitnessAttestationResult> AttestAsync(
        DrawProof proof,
        CancellationToken cancellationToken);

    Task<DrawProof> NotarizeAsync(
        FormalNotarizationRequest request,
        CancellationToken cancellationToken);
}
