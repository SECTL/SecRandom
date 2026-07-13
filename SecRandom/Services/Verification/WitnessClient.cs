using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class WitnessClient(
    HttpClient httpClient,
    MainConfigHandler configHandler) : IWitnessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public async Task<(WitnessChallengeTicket Ticket, string Token)> CreateChallengeAsync(
        byte[] inputHash,
        byte[] clientNonce,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            algorithmId = VerificationWireCodec.AlgorithmId,
            kernelVersion = VerificationWireCodec.KernelVersion,
            inputHash = ToBase64Url(inputHash),
            clientCommit = ToBase64Url(SHA256.HashData(clientNonce)),
            subject = new { type = "offline-user-id", id = configHandler.Data.General.Basic.OfflineUserId.ToString("D") },
            clientVersion = GlobalConstants.Version
        };

        using var response = await httpClient.PostAsJsonAsync(new Uri(new Uri(WitnessServiceUrl), "v1/challenges"), request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<WitnessChallengeResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Witness service returned an empty challenge response.");
        var ticket = VerifyToken<WitnessChallengeTicket>(envelope.Token);

        if (ticket.InputHash != request.inputHash || ticket.ClientCommit != request.clientCommit ||
            ticket.SubjectId != request.subject.id || ticket.AlgorithmId != request.algorithmId ||
            ticket.KernelVersion != request.kernelVersion || ticket.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Witness challenge is not bound to this frozen draw input.");

        return (ticket, envelope.Token);
    }

    public async Task<(WitnessTicket Ticket, string Token)> CreateTicketAsync(CancellationToken cancellationToken)
    {
        var request = new
        {
            subject = new { type = "offline-user-id", id = configHandler.Data.General.Basic.OfflineUserId.ToString("D") },
            clientVersion = GlobalConstants.Version
        };

        using var response = await httpClient.PostAsJsonAsync(new Uri(new Uri(WitnessServiceUrl), "v1/tickets"), request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<WitnessTicketResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Witness service returned an empty ticket response.");
        var ticket = VerifyToken<WitnessTicket>(envelope.Token);
        if (ticket.SubjectId != request.subject.id || ticket.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            throw new InvalidDataException("Witness ticket is not bound to this installation.");
        return (ticket, envelope.Token);
    }

    public async Task<string> FinalizeAsync(
        string ticketToken,
        byte[] clientNonce,
        DrawProof proof,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            ticket = ticketToken,
            clientNonce = ToBase64Url(clientNonce),
            proof
        };

        using var response = await httpClient.PostAsJsonAsync(new Uri(new Uri(WitnessServiceUrl), "v1/proofs/finalize"), request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<WitnessFinalizeResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Witness service returned an empty finalization response.");
        var receipt = VerifyToken<WitnessReceipt>(envelope.Token);
        if (receipt.ProofId != proof.ProofId || receipt.InputHash != proof.InputHash ||
            receipt.PayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(proof.Payload))))
            throw new InvalidDataException("Witness receipt is not bound to this proof.");

        return envelope.Token;
    }

    private static T VerifyToken<T>(string token)
    {
        var parts = token.Split('.', StringSplitOptions.None);
        if (parts.Length != 2)
            throw new InvalidDataException("Witness token has an invalid format.");

        var payload = FromBase64Url(parts[0]);
        var signature = FromBase64Url(parts[1]);
        var publicKey = FromBase64Url(WitnessPublicKey);
        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out var bytesRead);
        if (bytesRead != publicKey.Length || !verifier.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new CryptographicException("Witness token signature is invalid.");

        return JsonSerializer.Deserialize<T>(payload, JsonOptions)
            ?? throw new InvalidDataException("Witness token payload is invalid.");
    }

    internal static string ToBase64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');

    internal static byte[] FromBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(normalized);
    }

    private const string WitnessServiceUrl = "https://fair.sectl.cn/";
    private const string WitnessPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcoho+hm/avirZMgRdkQak/ZpGuZtZWnXdFjvKTLj+dGa5jfkA7nsEg3H+t/ytDooxHFpWQ0I07u+CtZXgwMbog==";
}
