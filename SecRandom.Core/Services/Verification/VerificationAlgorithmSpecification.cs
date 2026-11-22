using System.Security.Cryptography;
using System.Text;
using SecRandom.Core.Models.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Services.Verification;

public sealed record VerificationAlgorithmSpecification(string Id, string Hash);

/// <summary>
///     Client-side registry for formally notarizable algorithm families. The server keeps an independent allow-list;
///     these hashes identify published specifications and do not attest to the binary running on this device.
/// </summary>
public static class VerificationAlgorithmSpecificationRegistry
{
    private const string EngineVersion = "3.6.0";

    private static readonly VerificationAlgorithmSpecification HistoryBalanced = Create(
        "secrandom-spec/history-balanced-weighted/chacha20/v1",
        "history-balanced-weighted",
        "history-balanced weighted sampling without replacement");

    private static readonly VerificationAlgorithmSpecification Weighted = Create(
        "secrandom-spec/weighted-without-replacement/chacha20/v1",
        "weighted-without-replacement",
        "fixed-point weighted sampling without replacement");

    private static readonly VerificationAlgorithmSpecification Inventory = Create(
        "secrandom-spec/inventory-partial-permutation/chacha20/v1",
        "inventory-partial-permutation",
        "uniform partial Fisher-Yates permutation");

    private static readonly VerificationAlgorithmSpecification GroupUniform = Create(
        "secrandom-spec/group-uniform-then-member-uniform/chacha20/v1",
        "group-uniform-then-member-uniform",
        "uniform group sampling without replacement followed by uniform member sampling within each selected group",
        "3.7.0");

    public static bool TryResolve(
        VerificationDrawKind kind,
        VerificationSamplingMode samplingMode,
        VerificationAlgorithmProfile profile,
        out VerificationAlgorithmSpecification specification)
    {
        specification = null!;
        if (!VerificationWireCodec.IsKindCompatible(profile, kind)
            || !VerificationWireCodec.IsSamplingModeCompatible(profile, samplingMode))
            return false;

        specification = (kind, samplingMode, profile) switch
        {
            (VerificationDrawKind.Student, VerificationSamplingMode.HistoryBalancedWeighted,
                VerificationAlgorithmProfile.StudentFairRepeat or VerificationAlgorithmProfile.StudentFairNoRepeat
                    or VerificationAlgorithmProfile.StudentFairHalfRepeat) => HistoryBalanced,
            (VerificationDrawKind.Student, VerificationSamplingMode.WeightedWithoutReplacement,
                VerificationAlgorithmProfile.StudentRandomRepeat or VerificationAlgorithmProfile.StudentRandomNoRepeat
                    or VerificationAlgorithmProfile.StudentRandomHalfRepeat) => Weighted,
            (VerificationDrawKind.Prize, VerificationSamplingMode.WeightedWithoutReplacement,
                VerificationAlgorithmProfile.LotteryPanRepeat or VerificationAlgorithmProfile.LotteryPanNoRepeat
                    or VerificationAlgorithmProfile.LotteryPanHalfRepeat) => Weighted,
            (VerificationDrawKind.Prize, VerificationSamplingMode.InventoryPermutation,
                VerificationAlgorithmProfile.LotteryInventoryCount) => Inventory,
            (VerificationDrawKind.Number, VerificationSamplingMode.InventoryPermutation,
                VerificationAlgorithmProfile.NumberUniformPermutation) => Inventory,
            (VerificationDrawKind.Student, VerificationSamplingMode.GroupUniform,
                VerificationAlgorithmProfile.StudentGroupUniform) => GroupUniform,
            _ => null!
        };

        return specification is not null;
    }

    private static VerificationAlgorithmSpecification Create(string id, string sampler, string rule, string? engineVersion = null)
    {
        var manifest = string.Join('\n',
            "SecRandom Algorithm Specification",
            "format=1",
            $"id={id}",
            $"engine={engineVersion ?? EngineVersion}",
            $"sampler={sampler}",
            "candidate-order=record-id-n-then-occurrence",
            "weight=nonnegative-micro-units",
            "guaranteed=selected-first-uniformly-without-replacement",
            $"rule={rule}") + "\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest))).ToLowerInvariant();
        return new VerificationAlgorithmSpecification(id, hash);
    }
}
