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

    [Fact]
    public void RecordedRemovalsOnlyAdvanceTheFloorOverAContiguousPrefix()
    {
        using var provider = CreateProvider();
        var chainStore = provider.GetRequiredService<ProofChainStore>();

        chainStore.RecordRemovedIndices([10], ProofChainEvent.StorageLimitCleanup);
        Assert.Equal(0, chainStore.Read().RetainedFromIndex);

        chainStore.RecordRemovedIndices([1], ProofChainEvent.StorageLimitCleanup);
        Assert.Equal(1, chainStore.Read().RetainedFromIndex);

        chainStore.RecordRemovedIndices([2], ProofChainEvent.RetentionCleanup);
        // Index 10 is recorded but still not contiguous, so the floor stops at 2 instead of jumping to 10.
        Assert.Equal(2, chainStore.Read().RetainedFromIndex);
    }

    [Fact]
    public void SparseCleanupCannotHideAnUnrecordedDeletion()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var chainStore = provider.GetRequiredService<ProofChainStore>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();

        var first = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());
        exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 5, 0, TimeSpan.Zero)), Context());
        var third = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 10, 0, TimeSpan.Zero)), Context());

        // The oldest proof disappears without being recorded, while an unrelated cleanup claims only the
        // newest index. That must not reclassify the surviving hole as retention-expired.
        File.Delete(first.Path);
        chainStore.RecordRemovedIndices([third.Proof.Chain!.Index], ProofChainEvent.StorageLimitCleanup);

        Assert.Equal(0, chainStore.Read().RetainedFromIndex);
        var report = verifier.Verify(TestContext.Current.CancellationToken);
        Assert.False(report.IsHealthy);
        Assert.Contains(report.Issues, issue => issue.Kind == ProofIntegrityIssueKind.Gap);
    }

    [Fact]
    public void StorageLimitCleanupKeepsTheProofItJustSaved()
    {
        using var provider = CreateProvider();
        var exporter = provider.GetRequiredService<DrawProofExportService>();
        var verifier = provider.GetRequiredService<ProofIntegrityVerifier>();
        var config = provider.GetRequiredService<MainConfigHandler>();

        config.Data.General.ProofRetention.RetentionDays = 0;
        // Far below the size of a single proof: the proof written by this save must still survive, otherwise
        // the chain head would point at a file that never landed and the attestation queue at a dead path.
        config.Data.General.ProofRetention.MaximumStorageBytes = 1;

        var saved = exporter.Save(CreateProof(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero)), Context());

        Assert.True(File.Exists(saved.Path));
        var report = verifier.Verify(TestContext.Current.CancellationToken);
        Assert.Equal(1, report.Chained);
        Assert.Equal(0, report.MissingTail);
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
