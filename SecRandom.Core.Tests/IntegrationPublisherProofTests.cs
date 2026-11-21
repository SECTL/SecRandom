using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecRandom.Services.Integration;

namespace SecRandom.Core.Tests;

public sealed class IntegrationPublisherProofTests
{
    [Fact]
    public void VerifyProof_AcceptsOfficialSignatureAndRejectsTampering()
    {
        using var signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var unsigned = new OfficialSdkProof(
            IntegrationChannelService.OfficialPackageId,
            "1.0.0",
            new string('a', 64),
            string.Empty);
        var canonical = $"secrandom.integration-sdk/v1\n{unsigned.PackageId}\n{unsigned.SdkVersion}\n{unsigned.AssemblySha256}";
        var proof = unsigned with
        {
            Signature = Convert.ToBase64String(
                signer.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256))
        };
        var publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());

        Assert.True(IntegrationChannelService.VerifyProof(proof, publicKey));
        Assert.False(IntegrationChannelService.VerifyProof(proof with { SdkVersion = "9.9.9" }, publicKey));
        Assert.False(IntegrationChannelService.VerifyProof(proof with { AssemblySha256 = "not-a-hash" }, publicKey));
        Assert.False(IntegrationChannelService.VerifyProof(proof with { PackageId = "unofficial.package" }, publicKey));
        Assert.False(IntegrationChannelService.VerifyProof(proof, null));
    }

    [Fact]
    public void IntegrationChannel_LegacySettings_DefaultBothCapabilitiesToEnabled()
    {
        const string json = """{"appId":"example.app","displayName":"Example","sdkVersion":"1.0.0","endpoint":"SecRandom_Integration_test","processId":42,"instanceId":"instance","approvedAt":"2026-11-21T00:00:00Z","lastSeen":"2026-11-21T00:00:00Z"}""";

        var channel = JsonSerializer.Deserialize<IntegrationChannel>(json, IntegrationJson.Options);

        Assert.NotNull(channel);
        Assert.True(channel.Enabled);
        Assert.True(channel.LinkageEnabled);
        Assert.Null(channel.CustomName);
        Assert.Equal("Example", channel.EffectiveName);
    }

    [Fact]
    public void IntegrationChannel_CustomSettings_RoundTripAndKeepOfficialDisplayNameSeparate()
    {
        var channel = new IntegrationChannel("example.app", "Official name", "1.0.0",
            "SecRandom_Integration_test", 42, "instance", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
        {
            CustomName = "Classroom display",
            Enabled = false,
            LinkageEnabled = false
        };

        var restored = JsonSerializer.Deserialize<IntegrationChannel>(
            JsonSerializer.Serialize(channel, IntegrationJson.Options), IntegrationJson.Options);

        Assert.NotNull(restored);
        Assert.Equal("Official name", restored.DisplayName);
        Assert.Equal("Classroom display", restored.EffectiveName);
        Assert.False(restored.Enabled);
        Assert.False(restored.LinkageEnabled);
    }
}
