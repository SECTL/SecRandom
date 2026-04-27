using System.Security.Cryptography;
using SecRandom.Core.Interfaces;

namespace SecRandom.Core.Services.Draw;

public sealed class CryptoRandomSource : IRandomSource
{
    public int NextInt32(int maxExclusive)
    {
        return RandomNumberGenerator.GetInt32(maxExclusive);
    }

    public double NextDouble()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);

        var value = BitConverter.ToUInt64(bytes);
        return (value >> 11) * (1.0 / (1UL << 53));
    }
}
