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

    // Present only when the service anchors local proof chains; older deployments omit it.
    public WitnessChainAnchor? Chain { get; init; }
}

/// <summary>
///     How the service compared this submission's chain position with what it previously recorded for the
///     same client. <c>behind</c> means the submitted chain is older than the anchor the service already
///     holds, which is what a deleted-and-rebuilt local chain looks like from the outside.
/// </summary>
public sealed class WitnessChainAnchor
{
    public string Status { get; init; } = string.Empty;
    public long? HeadIndex { get; init; }
    public string? HeadHash { get; init; }
    public int Breaks { get; init; }
}

public sealed record WitnessAttestationResult(string Receipt, WitnessChainAnchor? Chain);

public sealed class FormalNotarizationRequest
{
    public Guid ProofId { get; init; }
    public Guid? ParentProofId { get; init; }
    public string InputHash { get; init; } = string.Empty;
    public string ZeroSeedRequest { get; init; } = string.Empty;
    public string AuditPayload { get; init; } = string.Empty;
    public string ClientNonce { get; init; } = string.Empty;

    // The local chain head held before this notarization. The service only records it so the chain anchor
    // covers formal draws as well; it never takes part in the locked-input comparison.
    public long ChainIndex { get; init; }
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
