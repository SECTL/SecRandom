using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace SecRandom.Services.Auth;

/// <summary>
///     The public OIDC discovery document (<c>/.well-known/openid-configuration</c>), mapped to the parts
///     this client acts on: whether the service advertises the <c>openid</c> scope, and the published key
///     set a future id_token signature check reads its keys from.
/// </summary>
public sealed record SectlOidcConfiguration(
    [property: JsonPropertyName("issuer")] string? Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string? TokenEndpoint,
    [property: JsonPropertyName("userinfo_endpoint")] string? UserinfoEndpoint,
    [property: JsonPropertyName("jwks_uri")] string? JwksUri,
    [property: JsonPropertyName("introspection_endpoint")] string? IntrospectionEndpoint,
    [property: JsonPropertyName("scopes_supported")] string[]? ScopesSupported,
    [property: JsonPropertyName("id_token_signing_alg_values_supported")]
    string[]? IdTokenSigningAlgorithms)
{
    /// <summary>
    ///     Whether the service implements the OIDC branch. This is not an admission decision: the
    ///     discovery document is public and is served regardless of the site-wide and per-platform OIDC
    ///     switches, so an attempt that requests <c>openid</c> still fails with
    ///     <c>unauthorized_client</c> until an operator enables both of them for this platform.
    /// </summary>
    public bool SupportsOpenId =>
        ScopesSupported is not null && ScopesSupported.Contains("openid", StringComparer.Ordinal);
}

/// <summary>
///     The published key set (<c>jwks_uri</c>), i.e. the RSA keys an <c>id_token</c> signature must verify
///     against. Only what the client acts on is mapped: the verifier never trusts a key because it exists,
///     it trusts the one key whose <c>kid</c> the token names.
/// </summary>
public sealed record SectlJwkSet(
    [property: JsonPropertyName("keys")] SectlJwk[]? Keys)
{
    /// <summary>
    ///     Finds the signing key a token's header names. A key that is plainly for another purpose
    ///     (<c>use</c> other than <c>sig</c>, <c>alg</c> other than RS256) is skipped rather than trusted,
    ///     so a key set that also publishes encryption keys cannot widen what is accepted.
    /// </summary>
    public SectlJwk? FindSigningKey(string? keyId)
    {
        if (Keys is null)
            return null;

        return Keys.FirstOrDefault(key =>
            key.Kid is not null
            && string.Equals(key.Kid, keyId, StringComparison.Ordinal)
            && (key.Use is null || string.Equals(key.Use, "sig", StringComparison.Ordinal))
            && (key.Algorithm is null || string.Equals(key.Algorithm, "RS256", StringComparison.Ordinal)));
    }
}

/// <summary>One published key: an RSA public key in JWK form (modulus + exponent).</summary>
public sealed record SectlJwk(
    [property: JsonPropertyName("kty")] string? KeyType,
    [property: JsonPropertyName("kid")] string? Kid,
    [property: JsonPropertyName("use")] string? Use,
    [property: JsonPropertyName("alg")] string? Algorithm,
    [property: JsonPropertyName("n")] string? Modulus,
    [property: JsonPropertyName("e")] string? Exponent)
{
    /// <summary>
    ///     Imports the published modulus/exponent. Returns <see langword="null" /> for anything that is not
    ///     a usable RSA public key, so a malformed entry only makes that one key unusable instead of
    ///     faulting the whole verification path.
    /// </summary>
    public RSA? TryImportRsa()
    {
        if (!string.Equals(KeyType, "RSA", StringComparison.Ordinal))
            return null;

        var modulus = SectlIdTokenValidator.TryDecodeBase64Url(Modulus);
        var exponent = SectlIdTokenValidator.TryDecodeBase64Url(Exponent);
        if (modulus is null || exponent is null || modulus.Length == 0 || exponent.Length == 0)
            return null;

        try
        {
            var rsa = RSA.Create();
            rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });
            return rsa;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>
///     The answer of <c>POST /api/oauth/introspect</c>. A rejected, revoked, or deleted token is not an
///     error of this endpoint: it answers with <see cref="Active" /> = <see langword="false" />.
/// </summary>
public sealed record SectlTokenIntrospection(
    [property: JsonPropertyName("active")] bool Active,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("client_id")] string? ClientId,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("expires_at")] string? ExpiresAt,
    [property: JsonPropertyName("exp")] long? ExpiresAtUnixSeconds,
    [property: JsonPropertyName("iat")] long? IssuedAtUnixSeconds)
{
    /// <summary>
    ///     A token with no time-based end. The service reports that as the string <c>"-1"</c> together
    ///     with <c>exp: null</c>, and the value must never be parsed as a date — a date parser reads
    ///     <c>"-1"</c> as 2000-12-31, i.e. as long expired, which would throw away a fresh session.
    /// </summary>
    public bool NeverExpires =>
        string.Equals(ExpiresAt, "-1", StringComparison.Ordinal) && ExpiresAtUnixSeconds is null;
}
