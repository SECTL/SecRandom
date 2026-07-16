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

        if (!VerificationWireCodec.IsKindCompatible(input.AlgorithmProfile, input.Kind)
            || !VerificationWireCodec.IsSamplingModeCompatible(input.AlgorithmProfile, input.SamplingMode))
            throw new InvalidOperationException("Verification algorithm profile does not match the sampling mode.");

        var candidates = VerificationWireCodec.CanonicalizeCandidates(input).ToList();
        if (input.Count > candidates.Count)
            throw new InvalidOperationException("Draw count exceeds the frozen candidate pool.");
        if (input.SamplingMode == VerificationSamplingMode.InventoryPermutation
            && candidates.Any(candidate => candidate.IsGuaranteed || candidate.WeightMicros != 1_000_000))
            throw new InvalidOperationException("Inventory permutation requires equally weighted, unrestricted tickets.");

        var guaranteed = candidates.Where(candidate => candidate.IsGuaranteed).ToList();
        var winners = new List<VerificationWinner>(input.Count);
        var random = new VerificationChaCha20Random(seed);

        if (guaranteed.Count >= input.Count)
        {
            SelectWeighted(guaranteed, input.Count, random, winners, useUnitWeights: true);
            return new VerificationKernelResult { Winners = winners };
        }

        winners.AddRange(guaranteed.Select(candidate => new VerificationWinner(candidate.RecordId, candidate.OccurrenceIndex)));
        var remaining = candidates.Where(candidate => !candidate.IsGuaranteed).ToList();
        if (input.SamplingMode == VerificationSamplingMode.InventoryPermutation)
            SelectInventory(remaining, input.Count - winners.Count, random, winners);
        else
            SelectWeighted(remaining, input.Count - winners.Count, random, winners, useUnitWeights: false);
        return new VerificationKernelResult { Winners = winners };
    }

    private static void SelectWeighted(
        List<VerificationCandidate> pool,
        int count,
        VerificationChaCha20Random random,
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

    private static void SelectInventory(
        List<VerificationCandidate> pool,
        int count,
        VerificationChaCha20Random random,
        ICollection<VerificationWinner> winners)
    {
        for (var index = 0; index < count; index++)
        {
            var selectedIndex = checked(index + (int)random.NextBelow((ulong)(pool.Count - index)));
            (pool[index], pool[selectedIndex]) = (pool[selectedIndex], pool[index]);
            var selected = pool[index];
            winners.Add(new VerificationWinner(selected.RecordId, selected.OccurrenceIndex));
        }
    }
}
