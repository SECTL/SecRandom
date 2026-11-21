using System.Security.Cryptography;
using System.Text;
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
}
