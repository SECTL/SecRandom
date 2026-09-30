using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Verification;

/// <summary>
///     Requests an RFC 3161 time-stamp token over a proof digest. It is the only trust anchor that does not
///     depend on the SecRandom-operated verification service: the token proves that this exact digest existed
///     no later than the stamped time, so any later edit to the proof is detectable.
///     It cannot prove the draw itself happened at that time. Time stamping is always on — there is no
///     user-facing switch — and only the digest ever leaves the device.
/// </summary>
public interface ITimestampAuthorityClient
{
    bool IsEnabled { get; }

    Task<string> TimestampAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken);
}

public sealed class TimestampAuthorityClient(
    HttpClient httpClient,
    ILogger<TimestampAuthorityClient> logger) : ITimestampAuthorityClient
{
    private const int NonceLength = 16;

    /// <summary>
    ///     Reachable from mainland networks, unlike most free public authorities. The authority is fixed rather
    ///     than configurable: a user-facing URL would let a broken or hostile endpoint silently disable the anchor.
    /// </summary>
    public static readonly Uri Authority = new("https://tsa.wotrus.com/");

    // The operating-system trust store does not carry the WoTrus root on every host, so it is pinned here.
    // Cross-checked against the vendor's published root page: subject "CA WoTrus Root", serial
    // 0A004C7E44148A8F902C840C79E6F0D8, SHA-1 45F662953267038A0A2FF8C54C9E52DB4F2AE3D7, valid until 2043-01-22.
    // SHA-256 fingerprint: F2A39E3514AEAA48716E93F0CB776BEBC88E9DE415C0C1A0E3C36AD01AA06751
    private const string PinnedAuthorityRoot =
        "MIIFUDCCAzigAwIBAgIQCgBMfkQUio+QLIQMeebw2DANBgkqhkiG9w0BAQsFADBCMQswCQYDVQQGEwJDTjEaMBgGA1UECgwRV29UcnVzIENBIExpbWl0ZWQxFzAVBgNVBAMMDkNBIFdvVHJ1cyBSb290MB4XDTE4MDEyMjA2MTYxNloXDTQzMDEyMjA2MTYxNlowQjELMAkGA1UEBhMCQ04xGjAYBgNVBAoMEVdvVHJ1cyBDQSBMaW1pdGVkMRcwFQYDVQQDDA5DQSBXb1RydXMgUm9vdDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAKQLhZMzzxkAYnotlyve7cZYOUzbFb9jdTxGfYaMC+l0WoVgge3CmyS9Sjr22RRFmYzjpDfTJn6FCBZTJeqjtHtMYEe3zDJ/FAJ5HO2KP1hhL542NU5bIHJ0j7xSZyC93vXsJAMWe8NLArLn78T63P3CuD7VJ+1gr9kisc7e6GFF9OYok3QPfl0OA5KY1V9IK8bLUmEdM8nVaZKXvBHAaYQ2609Q+QCkubom51fogUrOlfgFyMR3ZhijmcYKsTz72+MDZVvTMKV120j1xI+vaKZ+2fDsv94m2BQbUmVa2GEs+kKQc8IGn36RdtzMQdIvd93eOEG+NkmQHQsXKFO+uUMLAcwLIhyxlZdUmavW31V266arC7PVfUKIn++yWrq8uXUPd2tKqqqwe+gF3R1L4LgZjNFuL/jvFFaTb4Laot0R6eOsiagWwj+NIp9b0Y6RvEbTMqEu9lFSacS2ILE62A2VR5e4Q7wnbU8u1Yg0xpJqwW5JkfFcd4C1lpPVhsWxk509o13uGxsKqkSKSbkTmsMToehpEJnLo/D/FVWMd7n/52SMgETc++Qb90Ykv1O9H26UbpwjzV6G6u2nkDzUKzUMXDwyZvallJQQNzjXHlqHOiNBDDrLPFR+ZcOR5i1xprZzwq4qdcppZiQE3b8TCmcFGQfx6gx+156RoY9Mv6TLAgMBAAGjQjBAMA4GA1UdDwEB/wQEAwIBBjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBSBOnh1wSVdpOvyFcw2FAL+XPE2GDANBgkqhkiG9w0BAQsFAAOCAgEAbq8Ws08WiSVhyEhX6iFrA/Ni/4qhP3oOMbr6xAP9OoXqCeVhsvvnbBUYr1MJyjECjfEpefRpco5Qe3vGMe++FvLRhlFjfFHyuq4FmBGdMn1NSTC2hl3cR0yCMHyRf62brv/l/oOK+anT3UZ3hA6z3cKoCKe94gvZ0zxTRCWjF7g7KT98KQtaMFLlmuAVcPYi7gm4iVDt+D3/p2enpZM3pNFsQIw20CZVSlggHgeXyyViNIXptpb0sOJ4PHJDSLoduKDP+dinUtRBTjUKp6KxJMikTFHOHBbKFhZrtW/jxLF9MqVQNNsuWxMbhBEMFRKSLV4KIS4lmc9JYmbqs2ps6AYtiZ/jAfXat7mpfE9Kt4LFcP/EYdBHIEYx+KqPzlEhZlRSFwIylh3gBOLatKLC77qtGGKTwrssNMsxotuLIGNAdPNGZFvkQjBXAFQBWrKPOmsTMd8TinmtC0j/mxBLyOdUvQZEdqvsoKXR+iRES4bZs1dG9d87bx8bA+LnXbc0vtsZXTx4JYxTXG1DfCIRie2pOTgoJZd7uEZcEvxwuMWLzrmgtAjuzettWQQChRqn0wTay5CnZjAWRzp5tVJJdF2/gCGe25wjkqS/4K/R/52dxIYtV9g4V51+63QnAbma5Pe+UQOmhkRyepYImdacTSwPC7TYmh7LFfk4nJ6DqCo=";

    private static readonly Lazy<X509Certificate2Collection> PinnedRoots = new(CreatePinnedRoots);

    public bool IsEnabled => true;

    public async Task<string> TimestampAsync(ReadOnlyMemory<byte> hash, CancellationToken cancellationToken)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var request = Rfc3161TimestampRequest.CreateFromHash(
            hash,
            HashAlgorithmName.SHA256,
            nonce: nonce,
            requestSignerCertificates: true);

        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var response = await httpClient.PostAsync(Authority, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var token = request.ProcessResponse(responseBytes, out _);
        if (!TryValidateToken(token, hash.Span, nonce, null, out var trustSource, out var error))
            throw new CryptographicException($"The time-stamp token is not valid for this proof: {error}");

        logger.LogInformation(
            "已获取抽取证明时间戳。Authority={Authority}，时间={Timestamp:O}，信任来源={TrustSource}",
            Authority, token.TokenInfo.Timestamp, trustSource);
        return Convert.ToBase64String(token.AsSignedCms().Encode());
    }

    /// <summary>
    ///     Validates a stored token against the proof digest it must bind. The chain is first built against the
    ///     operating-system trust store, then against the pinned authority root; revocation is not checked so
    ///     validation stays offline-capable.
    /// </summary>
    public static TimestampValidationResult Validate(
        string token,
        ReadOnlyMemory<byte> hash,
        X509Certificate2Collection? trustRoots = null)
    {
        Rfc3161TimestampToken? decoded;
        try
        {
            if (!Rfc3161TimestampToken.TryDecode(Convert.FromBase64String(token), out var parsed, out _))
                return TimestampValidationResult.Invalid("时间戳格式无法解析");

            decoded = parsed;
        }
        catch (FormatException)
        {
            return TimestampValidationResult.Invalid("时间戳不是有效的 Base64");
        }

        if (decoded is null)
            return TimestampValidationResult.Invalid("时间戳格式无法解析");

        return TryValidateToken(decoded, hash.Span, nonce: null, trustRoots, out var trustSource, out var error)
            ? TimestampValidationResult.Valid(decoded.TokenInfo.Timestamp, trustSource)
            : TimestampValidationResult.Invalid(error ?? "时间戳校验失败");
    }

    private static bool TryValidateToken(
        Rfc3161TimestampToken token,
        ReadOnlySpan<byte> hash,
        ReadOnlySpan<byte> nonce,
        X509Certificate2Collection? trustRoots,
        out TimestampTrustSource trustSource,
        out string? error)
    {
        trustSource = TimestampTrustSource.None;
        error = null;
        if (!token.VerifySignatureForHash(hash, HashAlgorithmName.SHA256, out var signerCertificate, null)
            || signerCertificate is null)
        {
            error = "时间戳签名与证明摘要不匹配";
            return false;
        }

        if (!nonce.IsEmpty)
        {
            var tokenNonce = token.TokenInfo.GetNonce();
            if (tokenNonce is null || !tokenNonce.Value.Span.SequenceEqual(nonce))
            {
                error = "时间戳 nonce 与本次请求不一致";
                return false;
            }
        }

        if (BuildChain(signerCertificate, null))
        {
            trustSource = TimestampTrustSource.OperatingSystem;
            return true;
        }

        var pinned = new X509Certificate2Collection();
        pinned.AddRange(PinnedRoots.Value);
        if (trustRoots is { Count: > 0 })
            pinned.AddRange(trustRoots);
        if (BuildChain(signerCertificate, pinned))
        {
            trustSource = TimestampTrustSource.Pinned;
            return true;
        }

        error = "时间戳签名证书无法链接到受信任的根证书（系统信任库或内置根）";
        return false;
    }

    private static bool BuildChain(X509Certificate2 signerCertificate, X509Certificate2Collection? customRoots)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (customRoots is { Count: > 0 })
        {
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.AddRange(customRoots);
        }

        return chain.Build(signerCertificate);
    }

    private static X509Certificate2Collection CreatePinnedRoots()
    {
        var roots = new X509Certificate2Collection
        {
            X509CertificateLoader.LoadCertificate(Convert.FromBase64String(PinnedAuthorityRoot))
        };
        return roots;
    }
}

public enum TimestampTrustSource
{
    None,
    OperatingSystem,
    Pinned
}

public sealed record TimestampValidationResult(
    bool IsValid,
    DateTimeOffset? Timestamp,
    TimestampTrustSource TrustSource,
    string? Error)
{
    public static TimestampValidationResult Valid(DateTimeOffset timestamp, TimestampTrustSource trustSource) =>
        new(true, timestamp, trustSource, null);

    public static TimestampValidationResult Invalid(string error) => new(false, null, TimestampTrustSource.None, error);
}
