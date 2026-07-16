using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Verification;
using SecRandom.Services.Config;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Services.Verification;

public sealed class WitnessClient(
    HttpClient httpClient,
    DeviceUuidStore deviceUuidStore,
    ILogger<WitnessClient> logger) : IWitnessClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) }
    };

    public async Task<string> AttestAsync(
        DrawProof proof,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(WitnessServiceUrl), "v1/proofs/attest"))
        {
            Content = JsonContent.Create(proof, options: JsonOptions)
        };
        AddClientId(request);
        using var response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Witness attestation failed with {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }
        var envelope = await response.Content.ReadFromJsonAsync<WitnessAttestationResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Witness service returned an empty attestation response.");
        var receipt = VerifyToken<WitnessReceipt>(envelope.Token);
        if (receipt.ProofId != proof.ProofId || receipt.InputHash != proof.InputHash ||
            receipt.PayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(proof.Payload))) ||
            receipt.AuditPayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(proof.AuditPayload))) ||
            receipt.ProofHash != ComputeAttestedProofHash(proof) ||
            receipt.Mode != proof.Mode)
            throw new InvalidDataException("Server attestation is not bound to this proof.");

        return envelope.Token;
    }

    public async Task<DrawProof> NotarizeAsync(
        FormalNotarizationRequest request,
        CancellationToken cancellationToken)
    {
        var lockedRequest = FromBase64Url(request.ZeroSeedRequest);
        if (lockedRequest.Length >= 17 && lockedRequest.AsSpan(0, 4).SequenceEqual("SRDQ"u8))
        {
            logger.LogDebug(
                "提交正式公证锁定请求：ProofId={ProofId}，协议版本={Version}，抽取种类={DrawKind}，抽样策略={SamplingMode}，算法配置={AlgorithmProfile}，请求数量={Count}，候选数={CandidateCount}，帧长度={FrameLength}。",
                request.ProofId,
                BinaryPrimitives.ReadUInt16LittleEndian(lockedRequest[4..]),
                lockedRequest[6],
                lockedRequest[7],
                lockedRequest[8],
                BinaryPrimitives.ReadUInt32LittleEndian(lockedRequest[9..]),
                BinaryPrimitives.ReadUInt32LittleEndian(lockedRequest[13..]),
                lockedRequest.Length);
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(WitnessServiceUrl), "v1/notarizations"))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        AddClientId(httpRequest);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException($"Formal notarization failed with {(int)response.StatusCode}: {error}", null, response.StatusCode);
        }
        var envelope = await response.Content.ReadFromJsonAsync<FormalNotarizationResponse>(JsonOptions, cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidDataException("Witness service returned an empty notarization response.");
        var proof = envelope.Proof;
        var receipt = VerifyToken<WitnessReceipt>(proof.Witness?.Receipt
            ?? throw new InvalidDataException("Formal notarization did not include a receipt."));
        if (proof.ProofId != request.ProofId || proof.ParentProofId != request.ParentProofId || proof.Mode != VerificationProofMode.OnlineWitnessed
            || proof.InputHash != request.InputHash || receipt.Role != "server-notary"
            || receipt.ProofId != proof.ProofId || receipt.InputHash != proof.InputHash
            || receipt.PayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(proof.Payload)))
            || receipt.AuditPayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(proof.AuditPayload)))
            || receipt.ProofHash != ComputeAttestedProofHash(proof)
            || receipt.Mode != proof.Mode)
            throw new InvalidDataException("Server notarization is not bound to the locked draw.");

        var payload = FromBase64Url(proof.Payload);
        if (payload.Length < 49 || !payload.AsSpan(0, 4).SequenceEqual("SRDQ"u8)
            || !FromBase64Url(proof.AuditPayload).AsSpan().SequenceEqual(FromBase64Url(request.AuditPayload))
            || !MatchesLockedRequest(payload, FromBase64Url(request.ZeroSeedRequest)))
            throw new InvalidDataException("Server notarization does not match the locked input frame.");

        var challenge = VerifyToken<FormalLockReceipt>(proof.Witness?.Challenge
            ?? throw new InvalidDataException("Formal notarization did not include a lock receipt."));
        if (challenge.Role != "server-notary-lock" || challenge.KeyId != proof.Witness.KeyId
            || challenge.ProofId != request.ProofId || challenge.ParentProofId != request.ParentProofId
            || challenge.InputHash != request.InputHash || challenge.ClientNonce != request.ClientNonce
            || challenge.RequestHash != ToBase64Url(SHA256.HashData(FromBase64Url(request.ZeroSeedRequest)))
            || challenge.AuditPayloadHash != ToBase64Url(SHA256.HashData(FromBase64Url(request.AuditPayload)))
            || FromBase64Url(challenge.ServerNonce).Length != 32)
            throw new InvalidDataException("Formal notarization lock receipt is not bound to this request.");

        return proof;
    }

    internal static byte[] DeriveFormalSeed(DrawProof proof)
    {
        var lockReceipt = VerifyToken<FormalLockReceipt>(proof.Witness?.Challenge
            ?? throw new InvalidDataException("Formal proof does not include a lock receipt."));
        return VerificationSeedDerivation.DeriveOnline(
            FromBase64Url(proof.InputHash), lockReceipt.TicketId,
            FromBase64Url(lockReceipt.ClientNonce), FromBase64Url(lockReceipt.ServerNonce));
    }

    private static bool MatchesLockedRequest(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> lockedRequest)
    {
        if (lockedRequest.Length < 49 || !lockedRequest[..4].SequenceEqual("SRDQ"u8))
            return false;

        var candidateCount = BinaryPrimitives.ReadUInt32LittleEndian(payload[13..]);
        var requestLength = checked(49 + (int)candidateCount * 45);
        if (requestLength != lockedRequest.Length || payload.Length <= requestLength)
            return false;

        var finalizedRequest = payload[..requestLength].ToArray();
        finalizedRequest.AsSpan(17, 32).Clear();
        return finalizedRequest.AsSpan().SequenceEqual(lockedRequest);
    }

    private void AddClientId(HttpRequestMessage request)
    {
        var clientId = deviceUuidStore.GetOrCreate();
        request.Headers.Add("X-SecRandom-Client-Id", clientId.ToString("D"));
    }

    internal static T VerifyToken<T>(string token)
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

    internal static string ComputeAttestedProofHash(DrawProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        ArgumentNullException.ThrowIfNull(proof.Result);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendString(hash, proof.Format);
        AppendString(hash, proof.ProofId.ToString("N"));
        AppendString(hash, proof.ParentProofId?.ToString("N"));
        AppendInt32(hash, (int)proof.Mode);
        AppendInt64(hash, proof.CreatedAtUtc.UtcDateTime.Ticks);
        AppendString(hash, proof.AlgorithmId);
        AppendString(hash, proof.AlgorithmEngineVersion);
        AppendString(hash, proof.LegacyKernelVersion);
        AppendString(hash, proof.InputHash);
        AppendString(hash, proof.Payload);
        AppendString(hash, proof.AuditPayload);
        AppendInt32(hash, proof.Result.WinnerRecordIds.Count);
        foreach (var winnerRecordId in proof.Result.WinnerRecordIds)
            AppendString(hash, winnerRecordId.ToString("N"));
        AppendString(hash, proof.Witness?.Challenge);
        AppendString(hash, proof.Witness?.KeyId);
        return ToBase64Url(hash.GetHashAndReset());
    }

    private static void AppendString(IncrementalHash hash, string? value)
    {
        if (value is null)
        {
            AppendInt32(hash, -1);
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value);
        AppendInt32(hash, bytes.Length);
        hash.AppendData(bytes);
    }

    private static void AppendInt32(IncrementalHash hash, int value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private static void AppendInt64(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }

    private const string WitnessServiceUrl = "https://fair.sectl.cn/";
    private const string WitnessPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcoho+hm/avirZMgRdkQak/ZpGuZtZWnXdFjvKTLj+dGa5jfkA7nsEg3H+t/ytDooxHFpWQ0I07u+CtZXgwMbog==";
}
