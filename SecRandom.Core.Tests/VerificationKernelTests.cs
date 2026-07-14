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
        Assert.Equal("3.1.0", VerificationWireCodec.AlgorithmEngineVersion);
    }

    [Fact]
    public void DrawProof_UsesAlgorithmEngineVersionAndReadsLegacyKernelVersion()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var current = new DrawProof { AlgorithmEngineVersion = "3.1.0" };
        var currentJson = JsonSerializer.Serialize(current, options);
        var legacy = JsonSerializer.Deserialize<DrawProof>("{\"kernelVersion\":\"1.0.0\"}", options);

        Assert.Contains("\"algorithmEngineVersion\":\"3.1.0\"", currentJson);
        Assert.DoesNotContain("\"kernelVersion\"", currentJson);
        Assert.Equal("1.0.0", legacy!.LegacyKernelVersion);
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
    public void InventoryPermutation_RejectsWeightsChangedByInternalRules()
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
    public void AttachedSettings_RestoresEnabledHundredPercentRuleFromPersistedJson()
    {
        var settingsId = Guid.Parse(GlobalConstants.BehindSceneAttachedSettings);
        var student = new Student();
        student.AttachedObjects[settingsId] = JsonSerializer.SerializeToElement(new
        {
            is_attach_settings_enabled = true,
            probability = 100d
        });

        var settings = student.GetAttachedObject<BehindSceneAttachedSettings>(settingsId);

        Assert.NotNull(settings);
        Assert.True(settings.IsAttachSettingsEnabled);
        Assert.Equal(100d, settings.Probability);
    }

    [Fact]
    public void AttachedSettings_PersistedJsonUsesSnakeCaseFieldNames()
    {
        var settings = new BehindSceneAttachedSettings
        {
            IsAttachSettingsEnabled = true,
            Probability = 100d
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        });

        Assert.Contains("\"is_attach_settings_enabled\"", json);
        Assert.Contains("\"probability\":100", json);
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
