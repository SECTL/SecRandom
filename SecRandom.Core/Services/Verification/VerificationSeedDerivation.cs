using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Core.Services.Verification;

public static class VerificationSeedDerivation
{
    private static readonly byte[] DomainSeparator = Encoding.ASCII.GetBytes("SecRandomProof/v3/seed");

    public static byte[] CreateCsprngSeed() => RandomNumberGenerator.GetBytes(32);

    public static byte[] CreateCsprngNonce() => RandomNumberGenerator.GetBytes(32);

    public static byte[] DeriveOnline(
        ReadOnlySpan<byte> inputHash,
        string ticketId,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce)
    {
        if (inputHash.Length != 32 || clientNonce.Length != 32 || serverNonce.Length != 32)
            throw new ArgumentException("Verification seed inputs must be 32 bytes.");
        if (string.IsNullOrWhiteSpace(ticketId))
            throw new ArgumentException("Ticket ID is required.", nameof(ticketId));

        var ticketBytes = Encoding.ASCII.GetBytes(ticketId);
        var material = new byte[DomainSeparator.Length + inputHash.Length + ticketBytes.Length + clientNonce.Length + serverNonce.Length];
        var offset = 0;
        DomainSeparator.CopyTo(material, offset);
        offset += DomainSeparator.Length;
        inputHash.CopyTo(material.AsSpan(offset));
        offset += inputHash.Length;
        ticketBytes.CopyTo(material, offset);
        offset += ticketBytes.Length;
        clientNonce.CopyTo(material.AsSpan(offset));
        offset += clientNonce.Length;
        serverNonce.CopyTo(material.AsSpan(offset));
        return SHA256.HashData(material);
    }
}
