using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Verification;

public static class VerificationSeedDerivation
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v1/seed");

    public static byte[] DeriveOnline(
        ReadOnlySpan<byte> inputHash,
        string challengeId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        if (inputHash.Length != 32 || clientNonce.Length != 32 || serverNonce.Length != 32)
            throw new ArgumentException("Verification seed inputs must be 32 bytes.");
        if (string.IsNullOrWhiteSpace(challengeId))
            throw new ArgumentException("Challenge ID is required.", nameof(challengeId));

        var challengeBytes = Encoding.ASCII.GetBytes(challengeId);
        var material = new byte[DomainSeparator.Length + inputHash.Length + challengeBytes.Length + clientNonce.Length + serverNonce.Length];
        var offset = 0;
        DomainSeparator.CopyTo(material, offset);
        offset += DomainSeparator.Length;
        inputHash.CopyTo(material.AsSpan(offset));
        offset += inputHash.Length;
        challengeBytes.CopyTo(material, offset);
        offset += challengeBytes.Length;
        clientNonce.CopyTo(material.AsSpan(offset));
        offset += clientNonce.Length;
        serverNonce.CopyTo(material.AsSpan(offset));
        return SHA256.HashData(material);
    }
}
