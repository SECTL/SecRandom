using System.Buffers.Binary;
using SecRandom.Core.Models.Verification;

namespace SecRandom.Core.Services.Verification;

/// <summary>
///     Deterministic C# implementation shared by production verification draws and tests.
/// </summary>
public sealed class ManagedVerificationKernel : IVerificationKernel
{
    public VerificationKernelResult Draw(VerificationDrawInput input, ReadOnlySpan<byte> seed)
    {
        if (seed.Length != 32)
            throw new ArgumentException("Verification seeds must contain exactly 32 bytes.", nameof(seed));

        var candidates = VerificationWireCodec.CanonicalizeCandidates(input).ToList();
        if (input.Count > candidates.Count)
            throw new InvalidOperationException("Draw count exceeds the frozen candidate pool.");

        var guaranteed = candidates.Where(candidate => candidate.IsGuaranteed).ToList();
        var winners = new List<VerificationWinner>(input.Count);
        var random = new ChaCha20Random(seed);

        if (guaranteed.Count >= input.Count)
        {
            Select(guaranteed, input.Count, random, winners, useUnitWeights: true);
            return new VerificationKernelResult { Winners = winners };
        }

        winners.AddRange(guaranteed.Select(candidate => new VerificationWinner(candidate.RecordId, candidate.OccurrenceIndex)));
        var remaining = candidates.Where(candidate => !candidate.IsGuaranteed).ToList();
        Select(remaining, input.Count - winners.Count, random, winners, useUnitWeights: false);
        return new VerificationKernelResult { Winners = winners };
    }

    private static void Select(
        List<VerificationCandidate> pool,
        int count,
        ChaCha20Random random,
        ICollection<VerificationWinner> winners,
        bool useUnitWeights)
    {
        for (var drawIndex = 0; drawIndex < count; drawIndex++)
        {
            ulong totalWeight = 0;
            foreach (var candidate in pool)
            {
                var weight = useUnitWeights ? 1UL : checked((ulong)candidate.WeightMicros);
                totalWeight = checked(totalWeight + weight);
            }

            if (totalWeight == 0)
                throw new InvalidOperationException("Frozen candidate pool has no eligible weight.");

            var randomWeight = random.NextBelow(totalWeight);
            var selectedIndex = -1;
            for (var index = 0; index < pool.Count; index++)
            {
                var weight = useUnitWeights ? 1UL : checked((ulong)pool[index].WeightMicros);
                if (randomWeight < weight)
                {
                    selectedIndex = index;
                    break;
                }

                randomWeight -= weight;
            }

            if (selectedIndex < 0)
                throw new InvalidOperationException("Verification sampler failed to choose a candidate.");

            var selected = pool[selectedIndex];
            pool.RemoveAt(selectedIndex);
            winners.Add(new VerificationWinner(selected.RecordId, selected.OccurrenceIndex));
        }
    }

    private sealed class ChaCha20Random
    {
        private static ReadOnlySpan<uint> Constants => [0x61707865, 0x3320646e, 0x79622d32, 0x6b206574];

        private readonly uint[] _state = new uint[16];
        private readonly uint[] _block = new uint[16];
        private int _blockOffset = 16;

        public ChaCha20Random(ReadOnlySpan<byte> seed)
        {
            Constants.CopyTo(_state);
            for (var index = 0; index < 8; index++)
                _state[index + 4] = BinaryPrimitives.ReadUInt32LittleEndian(seed.Slice(index * 4, 4));

            _state[12] = 1;
            _state[13] = 0x31565253; // "SRV1" in little-endian form.
            _state[14] = 1;
            _state[15] = 0;
        }

        public ulong NextBelow(ulong bound)
        {
            if (bound == 0)
                throw new ArgumentOutOfRangeException(nameof(bound));

            var discard = (ulong.MaxValue % bound + 1) % bound;
            var limit = ulong.MaxValue - discard;
            while (true)
            {
                var value = NextUInt64();
                if (value <= limit)
                    return value % bound;
            }
        }

        private ulong NextUInt64()
        {
            var low = NextUInt32();
            var high = NextUInt32();
            return low | ((ulong)high << 32);
        }

        private uint NextUInt32()
        {
            if (_blockOffset == _block.Length)
                Refill();
            return _block[_blockOffset++];
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
}
