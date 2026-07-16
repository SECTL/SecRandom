using System;
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

public sealed class FormalNotarizationRequest
{
    public Guid ProofId { get; init; }
    public Guid? ParentProofId { get; init; }
    public string InputHash { get; init; } = string.Empty;
    public string ZeroSeedRequest { get; init; } = string.Empty;
    public string AuditPayload { get; init; } = string.Empty;
    public string ClientNonce { get; init; } = string.Empty;
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
    public string ClientNonce { get; init; } = string.Empty;
    public string ServerNonce { get; init; } = string.Empty;
    public DateTimeOffset LockedAtUtc { get; init; }
    public string KeyId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
}

public interface IWitnessClient
{
    Task<string> AttestAsync(
        DrawProof proof,
        CancellationToken cancellationToken);

    Task<DrawProof> NotarizeAsync(
        FormalNotarizationRequest request,
        CancellationToken cancellationToken);
}
