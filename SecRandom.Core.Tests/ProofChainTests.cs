using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Verification;
using SecRandom.Shared;
using SecRandom.Shared.Models.Verification;

namespace SecRandom.Core.Tests;

public sealed class ProofChainTests : IDisposable
{
    private readonly string _dataRoot = Path.Combine(
        Path.GetTempPath(),
        "SecRandom",
        "proof-chain-tests",
        Guid.NewGuid().ToString("N"));

    public ProofChainTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public void SavedProofsFormAContinuousChain()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var chainStore = provider.GetRequiredService<ProofChainStore>();

        var first = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        var second = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 5, 0, TimeSpan.Zero)), Context());

        Assert.Equal(1, first.Proof.Chain!.Index);
        Assert.Equal(2, second.Proof.Chain!.Index);
        Assert.Equal(first.Proof.Chain.SelfHash, second.Proof.Chain.PrevHash);

        var head = chainStore.Read();
        Assert.Equal(2, head.HeadIndex);
        Assert.Equal(second.Proof.Chain.SelfHash, head.HeadHash);
    }

    [Fact]
    public void VerifierAcceptsAnIntactChainAndCountsLegacyProofs()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();

        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 5, 0, TimeSpan.Zero)), Context());
        exporter.SaveAtPath(PathFor("legacy.srproof.json"), CreateProof(new DateTimeOffset(2026, 9, 30, 1, 10, 0, TimeSpan.Zero)));

        var report = verifier.Verify(TestContext.Current.CancellationToken);

        Assert.True(report.IsHealthy);
        Assert.Equal(2, report.Chained);
        Assert.Equal(1, report.Unchained);
        Assert.Equal(3, report.Total);
    }

    [Fact]
    public void VerifierReportsAMissingProofInTheMiddleOfTheChain()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();

        var first = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 5, 0, TimeSpan.Zero)), Context());
        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 10, 0, TimeSpan.Zero)), Context());
        File.Delete(first.Path);

        var report = verifier.Verify(TestContext.Current.CancellationToken);

        Assert.False(report.IsHealthy);
        Assert.Equal(1, report.Gaps);
        Assert.Contains(report.Issues, issue => issue.Kind == ProofIntegrityIssueKind.Gap);
    }

    [Fact]
    public void VerifierReportsAProofWhoseContentWasRewritten()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();

        var saved = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        exporter.SaveAtPath(saved.Path, saved.Proof with { Payload = "rewritten-payload" });

        var report = verifier.Verify(TestContext.Current.CancellationToken);

        Assert.False(report.IsHealthy);
        Assert.Equal(1, report.Modified);
        Assert.Contains(report.Issues, issue => issue.Kind == ProofIntegrityIssueKind.Modified);
    }

    [Fact]
    public void RetentionCleanupIsRecordedSoExpiredProofsAreNotReportedAsGaps()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var chainStore = provider.GetRequiredService<ProofChainStore>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();
        var config = provider.GetRequiredService<MainConfigHandler>();

        var first = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        var second = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 5, 0, TimeSpan.Zero)), Context());

        var expired = DateTime.UtcNow.AddDays(-10);
        File.SetLastWriteTimeUtc(first.Path, expired);
        File.SetLastWriteTimeUtc(second.Path, expired);
        config.Data.General.ProofRetention.RetentionDays = 1;

        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 10, 0, TimeSpan.Zero)), Context());

        Assert.False(File.Exists(first.Path));
        Assert.False(File.Exists(second.Path));
        Assert.Equal(2, chainStore.Read().RetainedFromIndex);

        var report = verifier.Verify(TestContext.Current.CancellationToken);
        Assert.True(report.IsHealthy);
        Assert.Equal(0, report.Gaps);
    }

    [Fact]
    public void TimestampValidationRejectsAnythingThatIsNotAUsableToken()
    {
        var hash = SHA256.HashData("proof-digest"u8);

        Assert.False(TimestampAuthorityClient.Validate("not-base64!", hash).IsValid);
        Assert.False(TimestampAuthorityClient.Validate(Convert.ToBase64String("plain-text"u8), hash).IsValid);
        Assert.False(TimestampAuthorityClient.Validate(string.Empty, hash).IsValid);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddCoreRuntimeServices();
        services.AddSingleton<ProofChainStore>();
        services.AddSingleton<DrawProofExportService>();
        services.AddSingleton<ProofIntegrityVerifier>();
        return services.BuildServiceProvider();
    }

    private static DrawProofExportContext Context() => DrawProofExportContext.ForStudents("默认名单");

    private static string PathFor(string fileName) => Utils.GetFilePath("proofs", fileName);

    private static DrawProof CreateProof(DateTimeOffset createdAt)
    {
        return new DrawProof
        {
            ProofId = Guid.NewGuid(),
            Mode = VerificationProofMode.OfflineReproducible,
            CreatedAtUtc = createdAt,
            AlgorithmId = "secrandom-student-fair-repeat/v3",
            AlgorithmEngineVersion = "3.3.0",
            InputHash = "input-hash",
            Payload = "payload",
            AuditPayload = "audit-payload",
            Result = new DrawProofResult { WinnerRecordIds = [Guid.NewGuid()] }
        };
    }

    private static void ConfigureDataRootForTests(string dataRoot)
    {
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);
    }

    private static void ResetDataRootForTests()
    {
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);
    }

    private static MethodInfo GetUtilsMethod(string name)
    {
        return typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
               ?? throw new InvalidOperationException($"Utils.{name} was not found.");
    }
}
