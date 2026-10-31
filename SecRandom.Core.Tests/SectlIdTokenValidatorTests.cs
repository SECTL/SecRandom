using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Services.Auth;

namespace SecRandom.Core.Tests;

/// <summary>
///     Covers <see cref="SectlIdTokenValidator" /> end to end with locally generated RSA keys: every
///     rejection branch is reachable without a live service, which is the point of keeping verification
///     free of network and storage.
/// </summary>
public sealed class SectlIdTokenValidatorTests
{
    private const string Issuer = "https://appwrite.sectl.cn";
    private const string ClientId = "69c8cd6a0012dd3ea10a";
    private const string Nonce = "nonce-from-this-attempt";
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly RSA SigningKey = RSA.Create(2048);
    private static readonly RSA OtherKey = RSA.Create(2048);

    private static SectlIdTokenValidationParameters Parameters(string? nonce = Nonce) =>
        new(Issuer, ClientId, nonce);

    private static string PublicJwks(string kid = "test-kid") =>
        BuildJwks(kid, SigningKey);

    private static string BuildJwks(string kid, RSA key)
    {
        var parameters = key.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    alg = "RS256",
                    kid,
                    n = Base64Url(parameters.Modulus!),
                    e = Base64Url(parameters.Exponent!)
                }
            }
        });
    }

    private static SectlJwkSet ParseKeySet(string json) =>
        JsonSerializer.Deserialize<SectlJwkSet>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Builds a signed compact JWS, letting each test overrule exactly the claim it is about.</summary>
    private static string CreateToken(
        string? alg = "RS256",
        string? kid = "test-kid",
        RSA? signingKey = null,
        string? iss = Issuer,
        object? aud = null,
        string? azp = null,
        long? exp = null,
        long? iat = null,
        string? nonce = Nonce,
        string? sub = "user-1",
        bool omitSignature = false)
    {
        var header = new Dictionary<string, object?> { ["alg"] = alg, ["typ"] = "JWT" };
        if (kid is not null)
            header["kid"] = kid;

        var payload = new Dictionary<string, object?>
        {
            ["iss"] = iss,
            ["aud"] = aud ?? ClientId,
            ["azp"] = azp,
            ["exp"] = exp ?? Now.AddHours(1).ToUnixTimeSeconds(),
            ["iat"] = iat ?? Now.ToUnixTimeSeconds(),
            ["nonce"] = nonce,
            ["sub"] = sub
        };

        var headerPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadPart = Base64Url(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerPart}.{payloadPart}");

        if (omitSignature)
            return $"{headerPart}.{payloadPart}.";

        var signature = (signingKey ?? SigningKey).SignData(signingInput, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        return $"{headerPart}.{payloadPart}.{Base64Url(signature)}";
    }

    private static SectlIdTokenValidation Validate(string token, SectlIdTokenValidationParameters? parameters = null,
        string? keySet = null) =>
        SectlIdTokenValidator.Validate(token, ParseKeySet(keySet ?? PublicJwks()), parameters ?? Parameters(), Now);

    [Fact]
    public void Validate_WhenEverythingMatches_AcceptsAndExposesSubject()
    {
        var result = Validate(CreateToken());

        Assert.True(result.IsValid);
        Assert.Null(result.Rejection);
        Assert.Equal("user-1", result.Subject);
        Assert.Equal(Now.AddHours(1), result.ExpiresAt);
    }

    [Fact]
    public void Validate_WhenAudienceIsAnArrayContainingTheClientId_Accepts()
    {
        var result = Validate(CreateToken(aud: new[] { "another-client", ClientId }, azp: ClientId));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenSeveralAudiencesButAzpNamesAnotherClient_Rejects()
    {
        var result = Validate(CreateToken(aud: new[] { ClientId, "another-client" }, azp: "another-client"));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.InvalidAuthorizedParty, result.Rejection);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS512")]
    [InlineData("rs256")]
    public void Validate_WhenAlgorithmIsNotRs256_RejectsBeforeCheckingTheSignature(string alg)
    {
        // Each of these is a real attack shape: `none` skips the signature, `HS256` would have the verifier
        // use the public key as an HMAC secret, and the rest are simply not what the service publishes.
        var result = Validate(CreateToken(alg: alg));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.UnsupportedAlgorithm, result.Rejection);
    }

    [Fact]
    public void Validate_WhenSignatureIsAbsent_RejectsAsMalformed()
    {
        var result = Validate(CreateToken(omitSignature: true));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.Malformed, result.Rejection);
    }

    [Fact]
    public void Validate_WhenSignedByAnotherKey_RejectsTheSignature()
    {
        var result = Validate(CreateToken(signingKey: OtherKey));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.InvalidSignature, result.Rejection);
    }

    [Fact]
    public void Validate_WhenKeyIdIsUnknown_RejectsSoTheCallerCanRefetchTheKeySet()
    {
        var result = Validate(CreateToken(kid: "rotated-kid"));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.UnknownKeyId, result.Rejection);
    }

    [Fact]
    public void Validate_WhenIssuerDiffers_Rejects()
    {
        var result = Validate(CreateToken(iss: "https://sectl.cn"));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.InvalidIssuer, result.Rejection);
    }

    [Fact]
    public void Validate_WhenAudienceIsAnotherClient_Rejects()
    {
        var result = Validate(CreateToken(aud: "another-client"));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.InvalidAudience, result.Rejection);
    }

    [Fact]
    public void Validate_WhenExpiredBeyondTheClockSkew_Rejects()
    {
        var result = Validate(CreateToken(exp: Now.AddMinutes(-10).ToUnixTimeSeconds()));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.Expired, result.Rejection);
    }

    [Fact]
    public void Validate_WhenExpiredWithinTheClockSkew_Accepts()
    {
        var result = Validate(CreateToken(exp: Now.AddMinutes(-1).ToUnixTimeSeconds()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenIssuedInTheFutureBeyondTheClockSkew_Rejects()
    {
        var result = Validate(CreateToken(iat: Now.AddMinutes(30).ToUnixTimeSeconds()));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.IssuedInFuture, result.Rejection);
    }

    [Fact]
    public void Validate_WhenNonceDiffers_Rejects()
    {
        var result = Validate(CreateToken(nonce: "some-other-nonce"));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.NonceMismatch, result.Rejection);
    }

    [Fact]
    public void Validate_WhenNonceIsMissingFromTheToken_Rejects()
    {
        var result = Validate(CreateToken(nonce: null));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.NonceMismatch, result.Rejection);
    }

    [Fact]
    public void Validate_WhenTheAttemptSentNoNonce_SkipsTheNonceCheck()
    {
        // A plain OAuth authorization sends no nonce, so the claim has nothing to be compared against.
        var result = Validate(CreateToken(nonce: null), Parameters(nonce: null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WhenSubjectIsMissing_Rejects()
    {
        var result = Validate(CreateToken(sub: null));

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.MissingSubject, result.Rejection);
    }

    [Fact]
    public void Validate_WhenTokenIsNotACompactJws_RejectsAsMalformed()
    {
        Assert.Equal(SectlIdTokenRejection.Malformed, Validate("not-a-jwt").Rejection);
        Assert.Equal(SectlIdTokenRejection.Malformed, Validate("a.b.c").Rejection);
    }

    [Fact]
    public void Validate_WhenKeySetIsUnavailable_RejectsAsMalformed()
    {
        var result = SectlIdTokenValidator.Validate(CreateToken(), null, Parameters(), Now);

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.Malformed, result.Rejection);
    }

    [Fact]
    public void Validate_WhenTheKeyIsForEncryptionOnly_RejectsSoItCannotWidenWhatIsAccepted()
    {
        var keySet = ParseKeySet(JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "enc",
                    alg = "RSA-OAEP",
                    kid = "test-kid",
                    n = Base64Url(SigningKey.ExportParameters(false).Modulus!),
                    e = Base64Url(SigningKey.ExportParameters(false).Exponent!)
                }
            }
        }));

        var result = SectlIdTokenValidator.Validate(CreateToken(), keySet, Parameters(), Now);

        Assert.False(result.IsValid);
        Assert.Equal(SectlIdTokenRejection.UnknownKeyId, result.Rejection);
    }

    [Fact]
    public void FindSigningKey_WhenKidMatches_ReturnsThePublishedKey()
    {
        var keySet = ParseKeySet(PublicJwks("mjw-kid"));

        var key = keySet.FindSigningKey("mjw-kid");

        Assert.NotNull(key);
        using var rsa = key!.TryImportRsa();
        Assert.NotNull(rsa);
        Assert.Equal(SigningKey.ExportParameters(false).Modulus, rsa!.ExportParameters(false).Modulus);
    }
}
