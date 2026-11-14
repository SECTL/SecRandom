using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Services.DataRoot;

/// <summary>
///     Durable state of one data-root move. The journal lives beside the anchor (package root for a portable
///     package, the per-user anchor directory otherwise), never inside either data root, so a half-finished move can
///     always be recognised on the next start and the old directory is never deleted by the migration itself.
/// </summary>
public sealed class DataRootMigrationJournal
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public string TransactionId { get; set; } = string.Empty;

    public DataRootMigrationState State { get; set; } = DataRootMigrationState.Pending;

    public string SourceRoot { get; set; } = string.Empty;

    public string TargetRoot { get; set; } = string.Empty;

    public string? StagingRoot { get; set; }

    public string? SourceArchiveRoot { get; set; }

    public DataRootMode Mode { get; set; } = DataRootMode.Custom;

    public string? PackageKind { get; set; }

    public string? AnchorPath { get; set; }

    public string? ErrorMessage { get; set; }

    public int CopiedFileCount { get; set; }

    public long CopiedByteCount { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public enum DataRootMigrationState
{
    Pending = 0,
    Copying = 1,
    Verifying = 2,
    Committing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6
}

public static class DataRootMigrationJournalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string GetPath(string anchorDirectory)
    {
        return Path.Combine(anchorDirectory, DataRootPaths.JournalFileName);
    }

    public static DataRootMigrationJournal? Read(string journalPath)
    {
        try
        {
            if (!File.Exists(journalPath))
                return null;

            var journal = JsonSerializer.Deserialize<DataRootMigrationJournal>(
                File.ReadAllText(journalPath), JsonOptions);
            if (journal is null || journal.SchemaVersion != DataRootMigrationJournal.CurrentSchemaVersion)
                return null;

            return journal;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static void Write(string journalPath, DataRootMigrationJournal journal)
    {
        journal.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var directory = Path.GetDirectoryName(journalPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = journalPath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, journal, JsonOptions);
            stream.Flush(true);
        }

        File.Move(temporaryPath, journalPath, true);
    }

    public static void Delete(string journalPath)
    {
        try
        {
            if (File.Exists(journalPath))
                File.Delete(journalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A journal that cannot be deleted is harmless once its transaction is completed.
        }
    }
}