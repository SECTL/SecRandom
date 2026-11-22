using System.Security.Cryptography;
using System.Text.Json;
using SecRandom.Core;
using SecRandom.Core.Models.AttachedSettings;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Models.Profile;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

public sealed class VerificationKernelTests
{
    [Fact]
    public void ProofProtocol_UsesSecRandomHistoryBalancedAlgorithmIdentity()
    {
        Assert.Equal("secrandom-fairdraw-history-balanced-weighted-chacha20/v3", VerificationWireCodec.AlgorithmId);
        Assert.Equal("3.5.0", VerificationWireCodec.AlgorithmEngineVersion);
        Assert.Equal(
            "secrandom-inventory-permutation-chacha20/v3",
            VerificationWireCodec.GetAlgorithmId(VerificationSamplingMode.InventoryPermutation));
        Assert.Equal(
            "secrandom-lottery-weighted-without-replacement-chacha20/v3",
            VerificationWireCodec.GetAlgorithmId(VerificationSamplingMode.WeightedWithoutReplacement));
        Assert.Equal(
            "secrandom-student-fair-half-repeat/v3",
            VerificationWireCodec.GetAlgorithmId(VerificationAlgorithmProfile.StudentFairHalfRepeat));
        Assert.Equal(
            "secrandom-lottery-pan-no-repeat/v3",
            VerificationWireCodec.GetAlgorithmId(VerificationAlgorithmProfile.LotteryPanNoRepeat));
    }

    [Fact]
    public void DrawProof_UsesAlgorithmEngineVersionAndReadsLegacyKernelVersion()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var current = new DrawProof { AlgorithmEngineVersion = "3.2.0" };
        var currentJson = JsonSerializer.Serialize(current, options);
        var legacy = JsonSerializer.Deserialize<DrawProof>("{\"kernelVersion\":\"1.0.0\"}", options);

        Assert.Contains("\"algorithmEngineVersion\":\"3.2.0\"", currentJson);
        Assert.DoesNotContain("\"kernelVersion\"", currentJson);
        Assert.Equal("1.0.0", legacy!.LegacyKernelVersion);
    }

    [Fact]
    public void DrawProof_VersionsTheOrchestratedGenerationSeparately()
    {
        // 编排放不进 3.5.0 的版本链：现网 3.5 验证器既不认识 SequentialOrder 也不认识 profile 19/20，
        // 所以只有编排证明盖 3.6.0，普通抽取继续盖 3.5.0，普通抽取在服务端升级期间照常认证。
        Assert.Equal("3.5.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.HistoryBalancedWeighted));
        Assert.Equal("3.5.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.InventoryPermutation));
        Assert.Equal("3.5.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.WeightedWithoutReplacement));
        Assert.Equal("3.6.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.SequentialOrder));
        Assert.Equal("3.7.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.GroupUniform));
        Assert.Equal("3.6.0", VerificationWireCodec.OrchestratedAlgorithmEngineVersion);
        Assert.Equal("3.7.0", VerificationWireCodec.GroupUniformAlgorithmEngineVersion);
    }

    [Fact]
    public void FormalProofsUseThePublishedAlgorithmGeneration()
    {
        Assert.Equal("3.6.0", VerificationWireCodec.GetFormalEngineVersion());
        Assert.Equal("3.7.0", VerificationWireCodec.GetFormalEngineVersion(VerificationSamplingMode.GroupUniform));
        Assert.Equal("3.5.0", VerificationWireCodec.GetEngineVersion(VerificationSamplingMode.HistoryBalancedWeighted));
    }

    [Fact]
    public void FormalAlgorithmRegistryRejectsOrchestrationAndMismatchedKinds()
    {
        Assert.True(VerificationAlgorithmSpecificationRegistry.TryResolve(
            VerificationDrawKind.Student,
            VerificationSamplingMode.HistoryBalancedWeighted,
            VerificationAlgorithmProfile.StudentFairNoRepeat,
            out var fair));
        Assert.NotEmpty(fair.Id);
        Assert.False(VerificationAlgorithmSpecificationRegistry.TryResolve(
            VerificationDrawKind.Student,
            VerificationSamplingMode.SequentialOrder,
            VerificationAlgorithmProfile.StudentOrchestrated,
            out _));
        Assert.False(VerificationAlgorithmSpecificationRegistry.TryResolve(
            VerificationDrawKind.Number,
            VerificationSamplingMode.WeightedWithoutReplacement,
            VerificationAlgorithmProfile.LotteryPanNoRepeat,
            out _));
        Assert.True(VerificationAlgorithmSpecificationRegistry.TryResolve(
            VerificationDrawKind.Student,
            VerificationSamplingMode.GroupUniform,
            VerificationAlgorithmProfile.StudentGroupUniform,
            out var groups));
        Assert.Equal("secrandom-spec/group-uniform-then-member-uniform/chacha20/v1", groups.Id);
        Assert.Equal("065beadd3daa6a72d5c186fdc3250b5d6e9f56ded15677540e7bb632d47c312c", groups.Hash);
    }

    [Fact]
    public void CandidatePoolCommitmentBindsOccurrenceWeightAndGuarantee()
    {
        var recordId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var baseInput = CreateInput(new VerificationCandidate(recordId, 0, 1_000_000, false));
        var changedWeight = CreateInput(new VerificationCandidate(recordId, 0, 2_000_000, false));
        var changedGuarantee = CreateInput(new VerificationCandidate(recordId, 0, 1_000_000, true));
        var changedOccurrence = CreateInput(new VerificationCandidate(recordId, 1, 1_000_000, false));

        var commitment = VerificationWireCodec.ComputeCandidatePoolCommitment(baseInput);
        Assert.NotEqual(commitment, VerificationWireCodec.ComputeCandidatePoolCommitment(changedWeight));
        Assert.NotEqual(commitment, VerificationWireCodec.ComputeCandidatePoolCommitment(changedGuarantee));
        Assert.NotEqual(commitment, VerificationWireCodec.ComputeCandidatePoolCommitment(changedOccurrence));
    }

    [Fact]
    public void Draw_IsStableAcrossCandidateOrder()
    {
        var first = CreateInput(
            new VerificationCandidate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0, 1_000_000, false),
            new VerificationCandidate(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0, 2_000_000, false),
            new VerificationCandidate(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0, 3_000_000, false));
        var reversed = CreateInput(first.Candidates.Reverse().ToArray());
        var seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var kernel = new ManagedVerificationKernel();

        var firstResult = kernel.Draw(first, seed);
        var reversedResult = kernel.Draw(reversed, seed);

        Assert.Equal(firstResult.Winners, reversedResult.Winners);
        Assert.Equal(VerificationWireCodec.ComputeInputHash(first), VerificationWireCodec.ComputeInputHash(reversed));
    }

    [Fact]
    public void Draw_ConsumesGuaranteedCandidatesBeforeWeightedCandidates()
    {
        var guaranteed = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var weighted = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var input = CreateInput(
            new VerificationCandidate(guaranteed, 0, 1, true),
            new VerificationCandidate(weighted, 0, 1_000_000, false));

        var result = new ManagedVerificationKernel().Draw(input, new byte[32]);

        Assert.Equal(guaranteed, result.Winners[0].RecordId);
        Assert.Equal(weighted, result.Winners[1].RecordId);
    }

    [Fact]
    public void Draw_AlwaysSelectsGuaranteedCandidateForSingleDraw()
    {
        var guaranteed = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            Count = 1,
            Candidates =
            [
                new VerificationCandidate(guaranteed, 0, 1_000_000, true),
                new VerificationCandidate(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0, long.MaxValue, false)
            ]
        };

        var result = new ManagedVerificationKernel().Draw(input, RandomNumberGenerator.GetBytes(32));

        Assert.Equal(guaranteed, result.Winners[0].RecordId);
    }

    [Fact]
    public void InventoryPermutation_IsStableAndDrawsWithoutReplacement()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = VerificationSamplingMode.InventoryPermutation,
            AlgorithmProfile = VerificationAlgorithmProfile.LotteryInventoryCount,
            Count = 3,
            Candidates =
            [
                new VerificationCandidate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 1, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 0, 1_000_000, false)
            ]
        };
        var seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var kernel = new ManagedVerificationKernel();

        var first = kernel.Draw(input, seed);
        var repeated = kernel.Draw(input, seed);

        Assert.Equal(first.Winners, repeated.Winners);
        Assert.Equal(3, first.Winners.Count);
        Assert.Equal(3, first.Winners.Distinct().Count());
    }

    [Fact]
    public void GroupUniform_SelectsGroupsEquallyBeforeSelectingMembers()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.GroupUniform,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentGroupUniform,
            Count = 1,
            Candidates =
            [
                new VerificationCandidate(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), 1, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), 1, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), 2, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), 2, 1_000_000, false),
                new VerificationCandidate(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), 2, 1_000_000, false)
            ]
        };
        var seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var kernel = new ManagedVerificationKernel();

        var first = kernel.Draw(input, seed);
        var repeated = kernel.Draw(input, seed);
        Assert.Equal(first.Winners, repeated.Winners);

        var groupCounts = new int[3];
        var memberCounts = new Dictionary<Guid, int>();
        for (var seedIndex = 0; seedIndex < 6000; seedIndex++)
        {
            byte[] drawSeed = new byte[32];
            BitConverter.GetBytes(seedIndex).CopyTo(drawSeed, 0);
            var winner = kernel.Draw(input, drawSeed).Winners.Single();
            var candidate = input.Candidates.Single(item => item.RecordId == winner.RecordId
                && item.OccurrenceIndex == winner.OccurrenceIndex);
            groupCounts[candidate.OccurrenceIndex]++;
            memberCounts[winner.RecordId] = memberCounts.GetValueOrDefault(winner.RecordId) + 1;
        }

        Assert.All(groupCounts, count => Assert.InRange(count, 1800, 2200));
        Assert.Equal(6000, memberCounts.Values.Sum());
        var largestGroupMemberCounts = input.Candidates.Where(candidate => candidate.OccurrenceIndex == 2)
            .Select(candidate => memberCounts.GetValueOrDefault(candidate.RecordId)).ToArray();
        Assert.All(largestGroupMemberCounts, count => Assert.InRange(count, 500, 850));
    }

    [Fact]
    public void InventoryPermutation_RejectsNonUnitWeights()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = VerificationSamplingMode.InventoryPermutation,
            Count = 1,
            Candidates = [new VerificationCandidate(Guid.NewGuid(), 0, 500_000, false)]
        };

        Assert.Throws<InvalidOperationException>(() => new ManagedVerificationKernel().Draw(input, new byte[32]));
    }

    [Fact]
    public void AlgorithmProfile_RequiresItsCommittedSamplingMode()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.HistoryBalancedWeighted,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentRandomNoRepeat,
            Count = 1,
            Candidates = [new VerificationCandidate(Guid.NewGuid(), 0, 1_000_000, false)]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationWireCodec.EncodeDrawRequest(input, new byte[32]));
        Assert.Throws<InvalidOperationException>(() => new ManagedVerificationKernel().Draw(input, new byte[32]));
    }

    [Fact]
    public void AlgorithmProfile_RequiresItsDrawKind()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Prize,
            SamplingMode = VerificationSamplingMode.HistoryBalancedWeighted,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentFairNoRepeat,
            Count = 1,
            Candidates = [new VerificationCandidate(Guid.NewGuid(), 0, 1_000_000, false)]
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationWireCodec.EncodeDrawRequest(input, new byte[32]));
        Assert.Throws<InvalidOperationException>(() => new ManagedVerificationKernel().Draw(input, new byte[32]));
    }

    [Fact]
    public void AlgorithmProfile_IsCommittedIntoTheV3Request()
    {
        var candidate = new VerificationCandidate(Guid.NewGuid(), 0, 1_000_000, false);
        var noRepeat = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.HistoryBalancedWeighted,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentFairNoRepeat,
            Count = 1,
            Candidates = [candidate]
        };
        var halfRepeat = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            SamplingMode = VerificationSamplingMode.HistoryBalancedWeighted,
            AlgorithmProfile = VerificationAlgorithmProfile.StudentFairHalfRepeat,
            Count = 1,
            Candidates = [candidate]
        };

        var request = VerificationWireCodec.EncodeDrawRequest(halfRepeat, new byte[32]);

        Assert.Equal(VerificationWireCodec.RequestFormatVersion, BitConverter.ToUInt16(request, 4));
        Assert.Equal((byte)VerificationAlgorithmProfile.StudentFairHalfRepeat, request[8]);
        Assert.NotEqual(VerificationWireCodec.ComputeInputHash(noRepeat), VerificationWireCodec.ComputeInputHash(halfRepeat));
    }

    [Fact]
    public void AttachedMusicSettings_OverrideOnlyWhenEnabled()
    {
        var student = new Student();
        student.AttachedObjects[Guid.Parse(GlobalConstants.DrawMusicAttachedSettings)] = new DrawMusicAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            AnimationMusic = "animation.mp3",
            ResultMusic = "result.mp3"
        };

        Assert.Equal("animation.mp3", DrawMusicAttachedSettingsResolver.GetAnimationMusic(student, "$none"));
        Assert.Equal("result.mp3", DrawMusicAttachedSettingsResolver.GetResultMusic(student, "$none"));

        student.GetAttachedObject<DrawMusicAttachedSettings>(Guid.Parse(GlobalConstants.DrawMusicAttachedSettings))!
            .IsAttachSettingsEnabled = false;

        Assert.Equal("$none", DrawMusicAttachedSettingsResolver.GetAnimationMusic(student, "$none"));
        Assert.Equal("$none", DrawMusicAttachedSettingsResolver.GetResultMusic(student, "$none"));
    }

    [Fact]
    public void AttachedMusicSettings_RestoresPersistedTrackSelections()
    {
        var settingsId = Guid.Parse(GlobalConstants.DrawMusicAttachedSettings);
        var prize = new Prize();
        prize.AttachedObjects[settingsId] = JsonSerializer.SerializeToElement(new
        {
            is_attach_settings_enabled = true,
            animation_music = "animation.mp3",
            result_music = "result.wav"
        });

        var settings = prize.GetAttachedObject<DrawMusicAttachedSettings>(settingsId);

        Assert.NotNull(settings);
        Assert.True(settings.IsAttachSettingsEnabled);
        Assert.Equal("animation.mp3", settings.AnimationMusic);
        Assert.Equal("result.wav", settings.ResultMusic);
    }

    [Fact]
    public void Draw_RejectsPoolWithNoEligibleWeight()
    {
        var input = new VerificationDrawInput
        {
            Kind = VerificationDrawKind.Student,
            Count = 1,
            Candidates = [new VerificationCandidate(Guid.NewGuid(), 0, 0, false)]
        };

        Assert.Throws<InvalidOperationException>(() => new ManagedVerificationKernel().Draw(input, new byte[32]));
    }

    [Fact]
    public void OnlineSeed_BindsEveryChallengeInput()
    {
        var inputHash = SHA256.HashData("input"u8);
        var clientNonce = SHA256.HashData("client"u8);
        var serverNonce = SHA256.HashData("server"u8);

        var first = VerificationSeedDerivation.DeriveOnline(inputHash, "00000000-0000-4000-8000-000000000001", clientNonce, serverNonce);
        var second = VerificationSeedDerivation.DeriveOnline(inputHash, "00000000-0000-4000-8000-000000000001", clientNonce, serverNonce);
        var changed = VerificationSeedDerivation.DeriveOnline(inputHash, "00000000-0000-4000-8000-000000000002", clientNonce, serverNonce);

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void CsprngSeedAndNonce_AreFresh32ByteValues()
    {
        var seeds = Enumerable.Range(0, 64).Select(_ => VerificationSeedDerivation.CreateCsprngSeed()).ToArray();
        var nonces = Enumerable.Range(0, 64).Select(_ => VerificationSeedDerivation.CreateCsprngNonce()).ToArray();

        Assert.All(seeds, seed =>
        {
            Assert.Equal(32, seed.Length);
            Assert.Contains(seed, value => value != 0);
        });
        Assert.All(nonces, nonce =>
        {
            Assert.Equal(32, nonce.Length);
            Assert.Contains(nonce, value => value != 0);
        });
        Assert.Equal(seeds.Length, seeds.Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(nonces.Length, nonces.Select(Convert.ToHexString).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void RejectionSampler_DiscardsOutOfRangeValues()
    {
        const ulong bound = (1UL << 63) + 1;
        var limit = ulong.MaxValue - ((ulong.MaxValue % bound + 1) % bound);
        var values = new Queue<ulong>([limit + 1, 0]);

        var value = VerificationChaCha20Random.SampleBelow(bound, values.Dequeue);

        Assert.Equal(0UL, value);
        Assert.Empty(values);
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationChaCha20Random.SampleBelow(0, () => 0));
    }

    [Fact]
    public void VerificationChaCha20_IsDeterministicAcrossBlockBoundary()
    {
        var seed = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
        var first = new VerificationChaCha20Random(seed);
        var repeated = new VerificationChaCha20Random(seed);
        var changed = new VerificationChaCha20Random(seed.Select((value, index) => index == 0 ? (byte)(value ^ 1) : value).ToArray());
        var expected = Enumerable.Range(0, 17).Select(_ => first.NextUInt32()).ToArray();
        var repeatedValues = Enumerable.Range(0, 17).Select(_ => repeated.NextUInt32()).ToArray();
        var changedValues = Enumerable.Range(0, 17).Select(_ => changed.NextUInt32()).ToArray();

        Assert.Equal(
            "D0880430F195099044A4CC6E2069BF99C1A98A457706F0726384A21D3518A19CD53AEE85709038B5949AFFD9CE07135293BE492CDE1C74028F45763DE2F2F534B201A72B",
            Convert.ToHexString(expected.SelectMany(BitConverter.GetBytes).ToArray()));
        Assert.Equal(expected, repeatedValues);
        Assert.False(expected.SequenceEqual(changedValues));
    }

    [Fact]
    public void ResponseCodec_RejectsTrailingData()
    {
        var response = VerificationWireCodec.EncodeDrawResponse(
        [
            new VerificationWinner(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), 0)
        ]);
        var malformed = response.Append((byte)0).ToArray();

        Assert.Throws<InvalidDataException>(() => VerificationWireCodec.DecodeDrawResponse(malformed));
    }

    private static VerificationDrawInput CreateInput(params VerificationCandidate[] candidates) => new()
    {
        Kind = VerificationDrawKind.Student,
        Count = 2,
        Candidates = candidates
    };
}
