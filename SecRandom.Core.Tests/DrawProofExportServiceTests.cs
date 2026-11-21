using SecRandom.Services.Verification;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

public sealed class DrawProofExportServiceTests
{
    [Fact]
    public void CreateFileName_UsesChinaStandardTimeAndProofIdOnly()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 30, 12, 345, TimeSpan.Zero)
        };

        var fileName = DrawProofExportService.CreateFileName(proof);

        Assert.Equal("20260714_083012_345_12345678.srproof.json", fileName);
    }

    [Fact]
    public void CreateFileName_CarriesNoRosterFilterOrAlgorithmText()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            AlgorithmId = VerificationWireCodec.GetAlgorithmId(VerificationAlgorithmProfile.StudentRandomHalfRepeat)
        };

        var fileName = DrawProofExportService.CreateFileName(proof);

        // The file name is visible in every folder listing, archive, and support screenshot, so it must not say
        // more than the document does: the roster name, the filter values, and the readable algorithm label used
        // to leak there while the proof body stayed de-identified.
        Assert.DoesNotContain("高一", fileName);
        Assert.DoesNotContain("组别", fileName);
        Assert.DoesNotContain("性别", fileName);
        Assert.DoesNotContain("全部", fileName);
        Assert.DoesNotContain("点名", fileName);
        Assert.DoesNotContain('：', fileName);
        Assert.DoesNotContain('、', fileName);
    }

    [Fact]
    public void CreateFileName_KeepsTheFileNameWithinWindowsLimits()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var fileName = DrawProofExportService.CreateFileName(proof);

        // The name no longer embeds user text, so its length is fixed regardless of the roster or the filters.
        Assert.True(fileName.Length <= 64, fileName);
        Assert.Matches("^[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9a-f]{8}\\.srproof\\.json$", fileName);
    }
}