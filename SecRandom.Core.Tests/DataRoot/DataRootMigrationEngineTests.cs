using SecRandom.Shared.DataRoot;
using SecRandom.Core.Services.DataRoot;

namespace SecRandom.Core.Tests.DataRoot;

public sealed class DataRootMigrationEngineTests
{
    [Fact]
    public void Validate_RejectsATargetInsideTheRunningPayload()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var source = env.CreateDirectory("source");

        var error = DataRootMigrationEngine.Validate(
            source,
            Path.Combine(appDirectory, "data"),
            appDirectory,
            out var detail);

        Assert.Equal(DataRootMigrationErrorCode.TargetInsideApp, error);
        Assert.False(string.IsNullOrWhiteSpace(detail));
    }

    [Fact]
    public void Validate_RejectsTargetsThatNestWithTheSource()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var source = env.CreateDirectory("source");

        Assert.Equal(
            DataRootMigrationErrorCode.TargetInsideSource,
            DataRootMigrationEngine.Validate(
                source, Path.Combine(source, "nested"), appDirectory, out _));

        var existingTarget = env.CreateDirectory("existing");
        Assert.Equal(
            DataRootMigrationErrorCode.SourceInsideTarget,
            DataRootMigrationEngine.Validate(
                existingTarget, Path.GetDirectoryName(existingTarget)!, appDirectory, out _));
    }

    [Fact]
    public void Validate_RejectsANonEmptyTarget()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var source = env.CreateDirectory("source");
        var target = env.CreateDirectory("target");
        env.WriteFile("target/keep.txt", "x");

        var error = DataRootMigrationEngine.Validate(source, target, appDirectory, out _);

        Assert.Equal(DataRootMigrationErrorCode.TargetNotEmpty, error);
    }

    [Fact]
    public void Run_MovesDataRewritesRelativePathsAndKeepsTheSourceAsAnArchive()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var source = env.CreateDirectory("source");
        var target = env.PathOf("target");

        env.WriteFile("source/config/settings.json", "{\"language\":\"zh-CN\"}");
        var image = env.WriteFile("source/images/student.png", "png");
        env.WriteFile("source/list/students.json",
            "{\"ImagePath\":\"" + image.Replace("\\", "\\\\") + "\"}");
        env.WriteFile("source/proofs/attestation-queue.json",
            "{\"items\":[{\"path\":\"" + env.PathOf("source", "proofs", "a.srproof.json").Replace("\\", "\\\\")
            + "\"}]}");

        var anchorPath = env.PathOf("anchor", DataRootPaths.AnchorFileName);
        var anchorDirectory = Path.GetDirectoryName(anchorPath)!;
        var journalPath = DataRootMigrationJournalStore.GetPath(anchorDirectory);
        var transactionId = Guid.NewGuid().ToString("N");
        var journal = new DataRootMigrationJournal
        {
            TransactionId = transactionId,
            State = DataRootMigrationState.Pending,
            SourceRoot = source,
            TargetRoot = Path.GetFullPath(target),
            StagingRoot = DataRootMigrationEngine.GetStagingPath(target, transactionId),
            Mode = DataRootMode.Custom,
            AnchorPath = anchorPath
        };

        var engine = new DataRootMigrationEngine(journal, journalPath, appDirectory);
        var result = engine.Run(progress: null, CancellationToken.None);

        Assert.True(result.Success, result.Detail);
        Assert.Equal(DataRootMigrationErrorCode.None, result.Code);
        Assert.Equal(Path.GetFullPath(target), result.TargetRoot);
        Assert.True(File.Exists(env.PathOf("target", "config", "settings.json")));
        Assert.True(File.Exists(env.PathOf("target", "images", "student.png")));

        var students = env.ReadFile("target/list/students.json");
        Assert.Contains("\"ImagePath\":\"images/student.png\"", students, StringComparison.Ordinal);
        var queue = env.ReadFile("target/proofs/attestation-queue.json");
        Assert.Contains("\"path\":\"proofs/a.srproof.json\"", queue, StringComparison.Ordinal);

        var anchor = DataRootAnchorStore.Read(anchorPath);
        Assert.Equal(DataRootAnchorStatus.Valid, anchor.Status);
        Assert.Equal(DataRootMode.Custom, DataRootModeExtensions.Parse(anchor.Anchor!.Mode));

        Assert.False(Directory.Exists(source));
        Assert.NotNull(result.SourceArchiveRoot);
        Assert.True(Directory.Exists(result.SourceArchiveRoot));
        Assert.False(File.Exists(journalPath));
    }

    [Fact]
    public void Run_TargetThatIsNotWritable_ReportsTheFailureWithoutMovingAnything()
    {
        using var env = new DataRootTestEnvironment();
        var appDirectory = env.CreateDirectory("install");
        var source = env.CreateDirectory("source");
        env.WriteFile("source/config/settings.json", "{}");
        var anchorPath = env.PathOf("anchor", DataRootPaths.AnchorFileName);
        var journalPath = DataRootMigrationJournalStore.GetPath(Path.GetDirectoryName(anchorPath)!);
        var transactionId = Guid.NewGuid().ToString("N");
        var target = env.PathOf("blocked", "target");
        // A file where the parent directory should be makes the staging directory impossible to create.
        env.WriteFile("blocked", "not a directory");

        var journal = new DataRootMigrationJournal
        {
            TransactionId = transactionId,
            State = DataRootMigrationState.Pending,
            SourceRoot = source,
            TargetRoot = target,
            StagingRoot = DataRootMigrationEngine.GetStagingPath(target, transactionId),
            Mode = DataRootMode.Custom,
            AnchorPath = anchorPath
        };

        var result = new DataRootMigrationEngine(journal, journalPath, appDirectory)
            .Run(progress: null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(target));
    }
}