using System.Text.Json.Serialization;

namespace SecRandom.Services.Auth;

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
