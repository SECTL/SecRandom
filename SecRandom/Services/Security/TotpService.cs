using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SecRandom.Services.Security;

internal static class TotpService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var builder = new StringBuilder(32);
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        return builder.ToString();
    }

    public static string GetProvisioningUri(string secret)
    {
        return $"otpauth://totp/SecRandom:local?secret={secret}&issuer=SecRandom&period=30&digits=6";
    }

    public static bool Verify(string secret, string code, DateTimeOffset now)
    {
        if (code.Length != 6 || !code.All(char.IsDigit))
            return false;

        var timestamp = now.ToUnixTimeSeconds() / 30;
        for (var offset = -1L; offset <= 1; offset++)
        {
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(CreateCode(secret, timestamp + offset)),
                    Encoding.ASCII.GetBytes(code)))
                return true;
        }

        return false;
    }

    private static string CreateCode(string secret, long timestamp)
    {
        using var hmac = new HMACSHA1(DecodeBase32(secret));
        var counter = BitConverter.GetBytes(timestamp);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counter);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) |
                     (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        var buffer = 0;
        var bits = 0;
        var output = new List<byte>();
        foreach (var character in value.Trim().TrimEnd('=').ToUpperInvariant())
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
                throw new FormatException("Invalid Base32 secret.");
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8)
                continue;
            output.Add((byte)(buffer >> (bits - 8)));
            bits -= 8;
        }
        return output.ToArray();
    }
}
