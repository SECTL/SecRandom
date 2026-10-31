using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SecRandom.Services.Auth;

/// <summary>
///     Why an <c>id_token</c> could not be trusted. The caller shows one of these to the user instead of
///     a bare failure, so the reason a claim set was discarded stays visible in the log and the settings
///     surface rather than turning into "the identity looks odd".
/// </summary>
public enum SectlIdTokenRejection
{
    /// <summary>Not a compact JWS with three parts, or a part is not readable JSON.</summary>
    Malformed,

    /// <summary>The header names an algorithm other than RS256 — including <c>none</c> and every HMAC.</summary>
    UnsupportedAlgorithm,

    /// <summary>The published key set holds no key for the header's <c>kid</c>.</summary>
    UnknownKeyId,

    /// <summary>The signature does not verify against the published key.</summary>
    InvalidSignature,

    /// <summary><c>iss</c> is missing or is not the issuer the discovery document publishes.</summary>
    InvalidIssuer,

    /// <summary><c>aud</c> does not contain this client id.</summary>
    InvalidAudience,

    /// <summary>Several audiences are present and <c>azp</c> is missing or names another client.</summary>
    InvalidAuthorizedParty,

    /// <summary><c>exp</c> is missing or has already passed.</summary>
    Expired,

    /// <summary><c>iat</c> is missing or lies further in the future than the clock skew allows.</summary>
    IssuedInFuture,

    /// <summary><c>nonce</c> is missing or is not the value this authorization attempt sent.</summary>
    NonceMismatch,

    /// <summary>The token carries no <c>sub</c>, so it identifies nobody.</summary>
    MissingSubject,

    /// <summary>The discovery document or its key set could not be read, so nothing can be proven.</summary>
    KeysUnavailable
}

/// <summary>
///     The outcome of verifying an <c>id_token</c>. <see cref="Subject" /> and <see cref="ExpiresAt" /> are
///     only populated for a token that verified, because those claims are what a caller would otherwise be
///     tempted to read from an unverified token.
/// </summary>
public sealed record SectlIdTokenValidation(
    bool IsValid,
    SectlIdTokenRejection? Rejection,
    string? Subject = null,
    DateTimeOffset? ExpiresAt = null)
{
    public static SectlIdTokenValidation Rejected(SectlIdTokenRejection rejection) => new(false, rejection);
}

/// <summary>
///     Everything the verifier may trust as input: the issuer and audience it must see, the nonce this
///     attempt sent, and how much clock skew it tolerates.
/// </summary>
public sealed record SectlIdTokenValidationParameters(
    string Issuer,
    string Audience,
    string? Nonce,
    TimeSpan? ClockSkew = null)
{
    public TimeSpan Skew => ClockSkew ?? TimeSpan.FromMinutes(5);
}

/// <summary>
///     Pure OIDC <c>id_token</c> verification. It deliberately has no network or storage dependency: the
///     caller fetches the discovery document and key set, and this type only decides whether a token can be
///     trusted. That is what makes every branch testable without a live service.
/// </summary>
/// <remarks>
///     The algorithm list is a single entry, not a preference. An <c>alg</c> taken from the token and
///     honoured as-is is the classic JWS confusion bug (<c>none</c> needs no signature, and <c>HS256</c>
///     would have the verifier treat the public key as an HMAC secret), so anything that is not RS256 is
///     rejected before a signature is even attempted.
/// </remarks>
public static class SectlIdTokenValidator
{
    private const string RequiredAlgorithm = "RS256";

    public static SectlIdTokenValidation Validate(
        string? idToken,
        SectlJwkSet? keySet,
        SectlIdTokenValidationParameters parameters,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(idToken) || keySet is null)
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.Malformed);

        var parts = idToken.Split('.');
        if (parts.Length != 3)
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.Malformed);

        JsonDocument header;
        JsonDocument payload;
        byte[] signature;
        try
        {
            header = JsonDocument.Parse(TryDecodeBase64Url(parts[0]) ?? throw new FormatException());
            payload = JsonDocument.Parse(TryDecodeBase64Url(parts[1]) ?? throw new FormatException());
            signature = TryDecodeBase64Url(parts[2]) ?? throw new FormatException();
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.Malformed);
        }

        using (header)
        using (payload)
        {
            var algorithm = ReadString(header.RootElement, "alg");
            if (!string.Equals(algorithm, RequiredAlgorithm, StringComparison.Ordinal))
                return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.UnsupportedAlgorithm);

            var keyId = ReadString(header.RootElement, "kid");
            var key = keySet.FindSigningKey(keyId);
            if (key is null)
                return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.UnknownKeyId);

            using var rsa = key.TryImportRsa();
            if (rsa is null)
                return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.InvalidSignature);

            var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
            var verified = rsa.VerifyData(signingInput, signature, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            if (!verified)
                return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.InvalidSignature);

            return ValidateClaims(payload.RootElement, parameters, now ?? DateTimeOffset.UtcNow);
        }
    }

    private static SectlIdTokenValidation ValidateClaims(JsonElement claims,
        SectlIdTokenValidationParameters parameters, DateTimeOffset now)
    {
        if (!string.Equals(ReadString(claims, "iss"), parameters.Issuer, StringComparison.Ordinal))
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.InvalidIssuer);

        var audiences = ReadAudiences(claims);
        if (!audiences.Contains(parameters.Audience, StringComparer.Ordinal))
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.InvalidAudience);

        // OIDC requires azp once more than one audience is addressed: without it there is no way to tell
        // which of those parties the token was actually minted for.
        if (audiences.Count > 1
            && !string.Equals(ReadString(claims, "azp"), parameters.Audience, StringComparison.Ordinal))
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.InvalidAuthorizedParty);

        var skew = parameters.Skew;
        var expiresAt = ReadNumericDate(claims, "exp");
        if (expiresAt is null || DateTimeOffset.FromUnixTimeSeconds(expiresAt.Value) + skew < now)
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.Expired);

        var issuedAt = ReadNumericDate(claims, "iat");
        if (issuedAt is null || DateTimeOffset.FromUnixTimeSeconds(issuedAt.Value) - skew > now)
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.IssuedInFuture);

        // A nonce is only required when this attempt actually sent one, which happens exactly on the
        // openid branch; a plain OAuth authorization has no nonce to compare against.
        if (parameters.Nonce is not null
            && !string.Equals(ReadString(claims, "nonce"), parameters.Nonce, StringComparison.Ordinal))
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.NonceMismatch);

        var subject = ReadString(claims, "sub");
        if (string.IsNullOrWhiteSpace(subject))
            return SectlIdTokenValidation.Rejected(SectlIdTokenRejection.MissingSubject);

        return new SectlIdTokenValidation(true, null, subject,
            DateTimeOffset.FromUnixTimeSeconds(expiresAt.Value));
    }

    private static List<string> ReadAudiences(JsonElement claims)
    {
        var audiences = new List<string>();
        if (!claims.TryGetProperty("aud", out var aud))
            return audiences;

        switch (aud.ValueKind)
        {
            case JsonValueKind.String:
                var single = aud.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                    audiences.Add(single);
                break;
            case JsonValueKind.Array:
                foreach (var entry in aud.EnumerateArray())
                {
                    var value = entry.ValueKind == JsonValueKind.String ? entry.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(value))
                        audiences.Add(value);
                }

                break;
        }

        return audiences;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadNumericDate(JsonElement claims, string name) =>
        claims.ValueKind == JsonValueKind.Object
        && claims.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var seconds)
            ? seconds
            : null;

    /// <summary>
    ///     Decodes base64url (RFC 7515). <see cref="Convert.FromBase64String" /> rejects the URL alphabet
    ///     and the stripped padding, so both are restored here; a length that cannot be padded is not a
    ///     valid encoding at all and yields <see langword="null" />.
    /// </summary>
    internal static byte[]? TryDecodeBase64Url(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            2 => normalized + "==",
            3 => normalized + "=",
            1 => string.Empty,
            _ => normalized
        };
        if (string.IsNullOrEmpty(normalized))
            return null;

        try
        {
            return Convert.FromBase64String(normalized);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
