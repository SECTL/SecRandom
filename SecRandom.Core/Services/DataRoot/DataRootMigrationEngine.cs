using System.Diagnostics;
using System.Security.Cryptography;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Services.DataRoot;

public enum DataRootMigrationPhase
{
    Preparing = 0,
    Copying = 1,
    Verifying = 2,
    Committing = 3,
    Completed = 4,
    Failed = 5
}

public enum DataRootMigrationErrorCode
{
    None = 0,
    SourceMissing = 1,
    SourceEmpty = 2,
    TargetInvalid = 3,
    TargetEqualsSource = 4,
    TargetInsideSource = 5,
    SourceInsideTarget = 6,
    TargetInsideApp = 7,
    TargetNotEmpty = 8,
    TargetNotWritable = 9,
    AnchorUnavailable = 10,
    CopyFailed = 11,
    VerifyFailed = 12,
    CommitFailed = 13,
    Cancelled = 14,
    NotFound = 15
}

public sealed record DataRootMigrationProgress(
    DataRootMigrationPhase Phase,
    int CopiedFileCount,
    long CopiedByteCount,
    int TotalFileCount,
    long TotalByteCount,
    string? CurrentEntry);

public sealed record DataRootMigrationResult(
    bool Success,
    DataRootMigrationErrorCode Code,
    string? Detail,
    string TargetRoot,
    string? SourceArchiveRoot = null,
    string? Warning = null);

/// <summary>
///     Moves one data root to another location. The copy lands in a staging directory on the target volume, the
///     stored paths are rewritten there, every file is compared against the source, and only then is the staging
///     directory renamed into place and the anchor written. The source directory is renamed, never deleted, so a
///     failed or interrupted move always leaves a complete old root behind.
/// </summary>
public sealed class DataRootMigrationEngine
{
    public const string StagingSuffix = ".migrating-";
    public const string SourceArchiveSuffix = ".migrated-";
    private const long HashComparisonLimitBytes = 4 * 1024 * 1024;

    private readonly string _appDirectory;
    private readonly DataRootMigrationJournal _journal;
    private readonly string _journalPath;

    public DataRootMigrationEngine(DataRootMigrationJournal journal, string journalPath, string appDirectory)
    {
        _journal = journal;
        _journalPath = journalPath;
        _appDirectory = appDirectory;
    }

    public static string GetStagingPath(string targetRoot, string transactionId)
    {
        var suffix = transactionId[..Math.Min(8, transactionId.Length)];
        return Path.GetFullPath(Path.TrimEndingDirectorySeparator(targetRoot) + StagingSuffix + suffix);
    }

    /// <summary>Checks a move before it starts; the migration window and the settings page both use this.</summary>
    public static DataRootMigrationErrorCode Validate(
        string? sourceRoot,
        string? targetRoot,
        string? appDirectory,
        out string? detail)
    {
        detail = null;

        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            detail = "The target directory is empty.";
            return DataRootMigrationErrorCode.TargetInvalid;
        }

        string source;
        string target;
        try
        {
            source = string.IsNullOrWhiteSpace(sourceRoot) ? string.Empty : Path.GetFullPath(sourceRoot);
            target = Path.GetFullPath(targetRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        {
            detail = exception.Message;
            return DataRootMigrationErrorCode.TargetInvalid;
        }

        if (File.Exists(target))
        {
            detail = "The target path is an existing file.";
            return DataRootMigrationErrorCode.TargetInvalid;
        }

        if (source.Length > 0 && DataRootPaths.PathEquals(source, target))
        {
            detail = "The target directory is the current data directory.";
            return DataRootMigrationErrorCode.TargetEqualsSource;
        }

        if (source.Length > 0 && DataRootPaths.IsSameOrNested(source, target))
        {
            detail = "The target directory is inside the current data directory.";
            return DataRootMigrationErrorCode.TargetInsideSource;
        }

        if (source.Length > 0 && DataRootPaths.IsSameOrNested(target, source))
        {
            detail = "The current data directory is inside the target directory.";
            return DataRootMigrationErrorCode.SourceInsideTarget;
        }

        if (!string.IsNullOrWhiteSpace(appDirectory) && DataRootPaths.IsSameOrNested(appDirectory, target))
        {
            detail = "The target directory is inside the running application directory.";
            return DataRootMigrationErrorCode.TargetInsideApp;
        }

        if (Directory.Exists(target))
        {
            try
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                {
                    detail = "The target directory is not empty.";
                    return DataRootMigrationErrorCode.TargetNotEmpty;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                detail = exception.Message;
                return DataRootMigrationErrorCode.TargetNotWritable;
            }
        }

        var parent = Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(parent))
        {
            detail = "The target directory has no parent.";
            return DataRootMigrationErrorCode.TargetInvalid;
        }

        try
        {
            Directory.CreateDirectory(parent);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException)
        {
            detail = exception.Message;
            return DataRootMigrationErrorCode.TargetNotWritable;
        }

        if (!DataRootPaths.TryVerifyWritable(parent, out var writeError))
        {
            detail = writeError;
            return DataRootMigrationErrorCode.TargetNotWritable;
        }

        return DataRootMigrationErrorCode.None;
    }

    public DataRootMigrationResult Run(
        IProgress<DataRootMigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var validation = Validate(_journal.SourceRoot, _journal.TargetRoot, _appDirectory, out var validationDetail);
            if (validation != DataRootMigrationErrorCode.None)
                return Fail(progress, validation, validationDetail);

            if (string.IsNullOrWhiteSpace(_journal.AnchorPath))
            {
                return Fail(progress, DataRootMigrationErrorCode.AnchorUnavailable,
                    "The migration does not know where to write the data-root anchor.");
            }

            var target = Path.GetFullPath(_journal.TargetRoot);
            var source = string.IsNullOrWhiteSpace(_journal.SourceRoot)
                ? string.Empty
                : Path.GetFullPath(_journal.SourceRoot);
            var staging = _journal.StagingRoot is { Length: > 0 } recorded
                ? Path.GetFullPath(recorded)
                : GetStagingPath(target, _journal.TransactionId);

            _journal.StagingRoot = staging;

            // A previous run was interrupted after the staged copy had been moved into place.
            if (_journal.State == DataRootMigrationState.Committing)
            {
                if (Directory.Exists(target))
                    return FinishCommit(target, source, progress);

                if (Directory.Exists(staging))
                    return Commit(staging, target, source, progress);
            }

            progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Preparing, 0, 0, 0, 0, null));

            if (!PrepareTarget(target, out var prepareError))
                return Fail(progress, prepareError, "The target directory could not be prepared.");

            if (Directory.Exists(staging))
                Directory.Delete(staging, true);

            var entries = EnumerateSource(source, out var sourceExists);
            if (sourceExists && entries.Count == 0)
            {
                // Nothing to carry over (only caches and temporary files); the move is still meaningful.
            }

            var totalBytes = entries.Sum(entry => entry.Length);
            _journal.State = DataRootMigrationState.Copying;
            Save();

            if (entries.Count > 0)
            {
                Directory.CreateDirectory(staging);
                CopyEntries(entries, staging, totalBytes, progress, cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(staging);
            }

            _journal.CopiedFileCount = entries.Count;
            _journal.CopiedByteCount = totalBytes;

            _journal.State = DataRootMigrationState.Verifying;
            Save();
            progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Verifying, entries.Count,
                totalBytes, entries.Count, totalBytes, null));

            var mismatch = Verify(entries, staging, source, cancellationToken);
            if (mismatch is not null)
                return Fail(progress, DataRootMigrationErrorCode.VerifyFailed, mismatch);

            var rewritten = DataRootPathRewriter.RewriteStaging(staging, source, target);
            if (rewritten > 0)
                _journal.ErrorMessage = null;

            return Commit(staging, target, source, progress);
        }
        catch (OperationCanceledException)
        {
            _journal.State = DataRootMigrationState.Cancelled;
            Save();
            return new DataRootMigrationResult(false, DataRootMigrationErrorCode.Cancelled, null,
                _journal.TargetRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException)
        {
            return Fail(progress, DataRootMigrationErrorCode.CommitFailed, exception.Message);
        }
    }

    private bool PrepareTarget(string target, out DataRootMigrationErrorCode error)
    {
        error = DataRootMigrationErrorCode.None;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = DataRootMigrationErrorCode.TargetNotWritable;
            return false;
        }

        return true;
    }

    private static List<SourceEntry> EnumerateSource(string source, out bool exists)
    {
        var entries = new List<SourceEntry>();
        if (source.Length == 0 || !Directory.Exists(source))
        {
            exists = false;
            return entries;
        }

        exists = true;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (ShouldSkip(relative))
                continue;

            long length;
            try
            {
                length = new FileInfo(file).Length;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            entries.Add(new SourceEntry(file, relative, length));
        }

        return entries;
    }

    private static bool ShouldSkip(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        var segments = normalized.Split('/');

        if (segments[0].StartsWith('.') || segments[0].StartsWith("updates", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = segments[^1];
        if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            return true;

        return segments.Any(segment => segment.StartsWith(SourceArchiveSuffix.TrimStart('.') + "-",
            StringComparison.OrdinalIgnoreCase));
    }

    private void CopyEntries(
        IReadOnlyList<SourceEntry> entries,
        string staging,
        long totalBytes,
        IProgress<DataRootMigrationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var copiedBytes = 0L;
        var copiedFiles = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = Path.Combine(staging, entry.RelativePath);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.Copy(entry.SourcePath, destination, false);
            try
            {
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(entry.SourcePath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Timestamps are only used to verify large files; a filesystem that refuses the setter still copies.
            }

            copiedBytes += entry.Length;
            copiedFiles++;

            if (stopwatch.ElapsedMilliseconds >= 150 || copiedFiles == entries.Count)
            {
                stopwatch.Restart();
                progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Copying, copiedFiles,
                    copiedBytes, entries.Count, totalBytes, entry.RelativePath));
            }

            _journal.CopiedFileCount = copiedFiles;
            _journal.CopiedByteCount = copiedBytes;
            if (copiedFiles % 200 == 0)
                Save();
        }

        Save();
    }

    private static string? Verify(
        IReadOnlyList<SourceEntry> entries,
        string staging,
        string source,
        CancellationToken cancellationToken)
    {
        var mismatches = new List<string>();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var copy = Path.Combine(staging, entry.RelativePath);
            if (!File.Exists(copy))
            {
                mismatches.Add($"{entry.RelativePath}: missing from the target copy");
                continue;
            }

            var copyLength = new FileInfo(copy).Length;
            if (copyLength != entry.Length)
            {
                mismatches.Add($"{entry.RelativePath}: expected {entry.Length} bytes, copied {copyLength}");
                continue;
            }

            if (entry.Length <= HashComparisonLimitBytes)
            {
                if (!string.Equals(HashFile(entry.SourcePath), HashFile(copy), StringComparison.Ordinal))
                    mismatches.Add($"{entry.RelativePath}: content hash differs");
            }
            else
            {
                var sourceWrite = File.GetLastWriteTimeUtc(entry.SourcePath);
                var copyWrite = File.GetLastWriteTimeUtc(copy);
                if (sourceWrite != copyWrite)
                    mismatches.Add($"{entry.RelativePath}: last write time differs");
            }

            if (mismatches.Count >= 10)
                break;
        }

        return mismatches.Count == 0 ? null : string.Join(Environment.NewLine, mismatches);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private DataRootMigrationResult Commit(
        string staging,
        string target,
        string source,
        IProgress<DataRootMigrationProgress>? progress)
    {
        progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Committing, _journal.CopiedFileCount,
            _journal.CopiedByteCount, _journal.CopiedFileCount, _journal.CopiedByteCount, null));

        _journal.State = DataRootMigrationState.Committing;
        Save();

        try
        {
            if (!Directory.Exists(staging))
                return Fail(progress, DataRootMigrationErrorCode.CommitFailed,
                    "The staged copy disappeared before the move could be committed.");

            if (Directory.Exists(target))
            {
                if (Directory.EnumerateFileSystemEntries(target).Any())
                    return Fail(progress, DataRootMigrationErrorCode.TargetNotEmpty, null);

                Directory.Delete(target);
            }

            Directory.Move(staging, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail(progress, DataRootMigrationErrorCode.CommitFailed, exception.Message);
        }

        try
        {
            WriteAnchor(target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException)
        {
            // The anchor write is the single commit point: without it the new location was never adopted, so put
            // the staged copy back and leave the source untouched.
            try
            {
                Directory.Move(target, staging);
            }
            catch (Exception restoreException) when (restoreException is IOException or UnauthorizedAccessException)
            {
                return Fail(progress, DataRootMigrationErrorCode.CommitFailed,
                    $"Anchor write failed ({exception.Message}) and the staged copy could not be restored " +
                    $"({restoreException.Message}).");
            }

            return Fail(progress, DataRootMigrationErrorCode.AnchorUnavailable, exception.Message);
        }

        return FinishCommit(target, source, progress);
    }

    private DataRootMigrationResult FinishCommit(
        string target,
        string source,
        IProgress<DataRootMigrationProgress>? progress)
    {
        WriteAnchor(target);

        string? warning = null;
        if (source.Length > 0 && Directory.Exists(source))
        {
            var archivePath = BuildSourceArchivePath(source);
            try
            {
                Directory.Move(source, archivePath);
                _journal.SourceArchiveRoot = archivePath;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                warning = $"The old data directory could not be renamed: {exception.Message}";
            }
        }

        _journal.State = DataRootMigrationState.Completed;
        Save();
        DataRootMigrationJournalStore.Delete(_journalPath);

        progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Completed, _journal.CopiedFileCount,
            _journal.CopiedByteCount, _journal.CopiedFileCount, _journal.CopiedByteCount, null));

        return new DataRootMigrationResult(true, DataRootMigrationErrorCode.None, null, target,
            _journal.SourceArchiveRoot, warning);
    }

    private void WriteAnchor(string target)
    {
        var anchorPath = _journal.AnchorPath!;
        var directory = Path.GetDirectoryName(anchorPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        DataRootAnchorStore.Write(anchorPath, new DataRootAnchor
        {
            Mode = _journal.Mode.ToStorageValue(),
            DataRoot = target,
            PackageKind = _journal.PackageKind,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });
    }

    private static string BuildSourceArchivePath(string source)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(source);
        var candidate = $"{trimmed}{SourceArchiveSuffix}{DateTime.Now:yyyyMMdd-HHmmss}";
        var index = 1;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = $"{trimmed}{SourceArchiveSuffix}{DateTime.Now:yyyyMMdd-HHmmss}-{index++}";

        return candidate;
    }

    private void Save()
    {
        DataRootMigrationJournalStore.Write(_journalPath, _journal);
    }

    private DataRootMigrationResult Fail(
        IProgress<DataRootMigrationProgress>? progress,
        DataRootMigrationErrorCode code,
        string? detail)
    {
        _journal.State = DataRootMigrationState.Failed;
        _journal.ErrorMessage = detail;
        Save();

        progress?.Report(new DataRootMigrationProgress(DataRootMigrationPhase.Failed, _journal.CopiedFileCount,
            _journal.CopiedByteCount, _journal.CopiedFileCount, _journal.CopiedByteCount, null));

        return new DataRootMigrationResult(false, code, detail, _journal.TargetRoot);
    }

    private sealed record SourceEntry(string SourcePath, string RelativePath, long Length);
}