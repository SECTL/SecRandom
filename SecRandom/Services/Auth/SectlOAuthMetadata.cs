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
