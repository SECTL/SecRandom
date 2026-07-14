using System.Buffers.Binary;

namespace SecRandom.Core.Services.Verification;

internal sealed class VerificationChaCha20Random
{
    private static ReadOnlySpan<uint> Constants => [0x61707865, 0x3320646e, 0x79622d32, 0x6b206574];

    private readonly uint[] _state = new uint[16];
    private readonly uint[] _block = new uint[16];
    private int _blockOffset = 16;

    internal VerificationChaCha20Random(ReadOnlySpan<byte> seed)
    {
        Constants.CopyTo(_state);
        for (var index = 0; index < 8; index++)
            _state[index + 4] = BinaryPrimitives.ReadUInt32LittleEndian(seed.Slice(index * 4, 4));

        _state[12] = 1;
        _state[13] = 0x31565253; // "SRV1" in little-endian form.
        _state[14] = 1;
        _state[15] = 0;
    }

    internal ulong NextBelow(ulong bound) => SampleBelow(bound, NextUInt64);

    internal static ulong SampleBelow(ulong bound, Func<ulong> nextUInt64)
    {
        if (bound == 0)
            throw new ArgumentOutOfRangeException(nameof(bound));

        var discard = (ulong.MaxValue % bound + 1) % bound;
        var limit = ulong.MaxValue - discard;
        while (true)
        {
            var value = nextUInt64();
            if (value <= limit)
                return value % bound;
        }
    }

    internal uint NextUInt32()
    {
        if (_blockOffset == _block.Length)
            Refill();
        return _block[_blockOffset++];
    }

    private ulong NextUInt64()
    {
        var low = NextUInt32();
        var high = NextUInt32();
        return low | ((ulong)high << 32);
    }

    private void Refill()
    {
        Array.Copy(_state, _block, _state.Length);
        for (var round = 0; round < 10; round++)
        {
            QuarterRound(_block, 0, 4, 8, 12);
            QuarterRound(_block, 1, 5, 9, 13);
            QuarterRound(_block, 2, 6, 10, 14);
            QuarterRound(_block, 3, 7, 11, 15);
            QuarterRound(_block, 0, 5, 10, 15);
            QuarterRound(_block, 1, 6, 11, 12);
            QuarterRound(_block, 2, 7, 8, 13);
            QuarterRound(_block, 3, 4, 9, 14);
        }

        for (var index = 0; index < _block.Length; index++)
            _block[index] += _state[index];

        _state[12]++;
        if (_state[12] == 0)
            _state[13]++;
        _blockOffset = 0;
    }

    private static void QuarterRound(uint[] state, int a, int b, int c, int d)
    {
        state[a] += state[b];
        state[d] = RotateLeft(state[d] ^ state[a], 16);
        state[c] += state[d];
        state[b] = RotateLeft(state[b] ^ state[c], 12);
        state[a] += state[b];
        state[d] = RotateLeft(state[d] ^ state[a], 8);
        state[c] += state[d];
        state[b] = RotateLeft(state[b] ^ state[c], 7);
    }

    private static uint RotateLeft(uint value, int amount) => (value << amount) | (value >> (32 - amount));
}
