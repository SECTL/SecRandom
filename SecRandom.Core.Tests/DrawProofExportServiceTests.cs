using SecRandom.Services.Verification;
using SecRandom.Core.Models.Verification;
using SecRandom.Core.Services.Verification;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

public sealed class DrawProofExportServiceTests
{
    [Fact]
    public void CreateFileName_UsesChinaStandardTimeListAndFilters()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 30, 12, 345, TimeSpan.Zero)
        };

        var fileName = DrawProofExportService.CreateFileName(
            proof,
            DrawProofExportContext.ForStudents("高一:一班", "A/组", "女"));

        Assert.Equal("20260714_083012_345_高一_一班_组别=A_组_性别=女_12345678.srproof.json", fileName);
    }

    [Fact]
    public void CreateFileName_UsesAllScopeWhenNoStudentFilterIsSelected()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero)
        };

        var fileName = DrawProofExportService.CreateFileName(proof, DrawProofExportContext.ForStudents("默认名单"));

        Assert.Contains("默认名单_全部_abcdef12", fileName);
    }

    [Fact]
    public void CreateFileName_UsesPrizePoolAndLotteryMode()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            AlgorithmId = VerificationWireCodec.InventoryLotteryAlgorithmId
        };

        var fileName = DrawProofExportService.CreateFileName(
            proof,
            DrawProofExportContext.ForPrizes("期末奖池"));

        Assert.Contains("期末奖池_按剩余数量_abcdef12", fileName);
        Assert.DoesNotContain("方式=", fileName);
        Assert.DoesNotContain("状态=", fileName);
    }

    [Fact]
    public void CreateFileName_UsesStudentAlgorithmProfile()
    {
        DrawProof proof = new()
        {
            ProofId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            AlgorithmId = VerificationWireCodec.GetAlgorithmId(VerificationAlgorithmProfile.StudentRandomHalfRepeat)
        };

        var fileName = DrawProofExportService.CreateFileName(
            proof,
            DrawProofExportContext.ForStudents("默认名单"));

        Assert.Contains("默认名单_点名_随机_半重复_abcdef12", fileName);
        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain(',', fileName);
        Assert.DoesNotContain('：', fileName);
        Assert.DoesNotContain('，', fileName);
        Assert.DoesNotContain('、', fileName);
    }

    [Theory]
    [InlineData("secrandom-lottery-inventory-count/v3")]
    [InlineData("secrandom-lottery-count-internal-rule/v3")]
    [InlineData("secrandom-lottery-pan-repeat/v3")]
    [InlineData("secrandom-lottery-pan-no-repeat/v3")]
    [InlineData("secrandom-lottery-pan-half-repeat/v3")]
    public void CreateFileName_RemovesPunctuationFromLotteryAlgorithmLabels(string algorithmId)
    {
        var proof = new DrawProof
        {
            ProofId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc"),
            CreatedAtUtc = new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            AlgorithmId = algorithmId
        };

        var fileName = DrawProofExportService.CreateFileName(
            proof,
            DrawProofExportContext.ForPrizes("期末奖池"));

        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain(',', fileName);
        Assert.DoesNotContain('：', fileName);
        Assert.DoesNotContain('，', fileName);
        Assert.DoesNotContain('、', fileName);
    }

    [Fact]
    public void CreateFileName_KeepsTheFileNameWithinWindowsLimits()
    {
        DrawProof proof = new();
        var context = new DrawProofExportContext(
            new string('名', 100),
            [new string('组', 100), new string('性', 100), new string('课', 100)]);

        var fileName = DrawProofExportService.CreateFileName(proof, context);

        Assert.True(fileName.Length <= 240);
    }
}
