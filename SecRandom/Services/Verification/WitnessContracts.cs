using System;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class WitnessChallengeTicket
{
    public string ChallengeId { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string ClientCommit { get; init; } = string.Empty;
    public string ServerNonce { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string AlgorithmId { get; init; } = string.Empty;
    public string KernelVersion { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string KeyId { get; init; } = string.Empty;
}

public sealed class WitnessChallengeResponse
{
    public string Token { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
}

public sealed class WitnessTicket
{
    public string TicketId { get; init; } = string.Empty;
    public string ServerNonce { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public string KeyId { get; init; } = string.Empty;
}

public sealed class WitnessTicketResponse
{
    public string Token { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
}

public sealed class WitnessReceipt
{
    public Guid ProofId { get; init; }
    public string ChallengeId { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string PayloadHash { get; init; } = string.Empty;
    public string SubjectId { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
    public DateTimeOffset FinalizedAtUtc { get; init; }
}

public sealed class WitnessFinalizeResponse
{
    public string Token { get; init; } = string.Empty;
    public string KeyId { get; init; } = string.Empty;
}

public interface IWitnessClient
{
    Task<(WitnessChallengeTicket Ticket, string Token)> CreateChallengeAsync(
        byte[] inputHash,
        byte[] clientNonce,
        CancellationToken cancellationToken);

    Task<(WitnessTicket Ticket, string Token)> CreateTicketAsync(CancellationToken cancellationToken);

    Task<string> FinalizeAsync(
        string ticketToken,
        byte[] clientNonce,
        DrawProof proof,
        CancellationToken cancellationToken);
}
