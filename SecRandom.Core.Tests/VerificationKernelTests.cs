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
