using System.Text.Json;
using Microsoft.Data.Sqlite;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Shared;

namespace SecRandom.Core.Services.Orchestration;

/// <summary>
///     SQLite store for all arranged draw lists and their runtime cursors. Legacy per-profile JSON is imported on read.
/// </summary>
/// <remarks>
///     Profiles and cursors use separate tables so editing a profile never overwrites a draw's cursor. Each table
///     update is transactional; old per-profile JSON files are retained as migration backups.
/// </remarks>
public interface IOrchestrationStore
{
    IReadOnlyList<OrchestrationProfile> LoadAll();

    OrchestrationProfile? Load(string id);

    void Save(OrchestrationProfile profile);

    void Delete(string id);

    /// <summary>Reads the cursor of <paramref name="profile" />, clamped into that profile's round range.</summary>
    OrchestrationRuntime GetRuntime(OrchestrationProfile profile);

    void SaveRuntime(OrchestrationRuntime runtime);

    IReadOnlyList<OrchestrationProfile> FindActive(OrchestrationTarget target, string listName);
}

public sealed class OrchestrationStore : IOrchestrationStore
{
    private const string FormatMarker = "SecRandomOrchestrationDatabase/v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public IReadOnlyList<OrchestrationProfile> LoadAll()
    {
        MigrateLegacyProfiles();
        var profiles = new List<OrchestrationProfile>();
        if (!File.Exists(DatabasePath()))
            return profiles;

        try
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            if (!IsCurrentDatabase(connection))
                return profiles;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM profiles";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var profile = ReadProfilePayload(reader.GetString(0));
                if (profile is not null)
                    profiles.Add(profile);
            }
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException)
        {
            return [];
        }

        profiles.Sort((left, right) =>
        {
            var byTarget = left.Target.CompareTo(right.Target);
            if (byTarget != 0)
                return byTarget;
            var byList = string.Compare(left.SourceListName, right.SourceListName, StringComparison.CurrentCulture);
            return byList != 0 ? byList : string.Compare(left.Name, right.Name, StringComparison.CurrentCulture);
        });
        return profiles;
    }

    public OrchestrationProfile? Load(string id)
    {
        if (!IsValidId(id))
            return null;
        MigrateLegacyProfiles();
        if (!File.Exists(DatabasePath()))
            return null;
        try
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            if (!IsCurrentDatabase(connection))
                return null;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload FROM profiles WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteScalar() is string payload ? ReadProfilePayload(payload) : null;
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException)
        {
            return null;
        }
    }

    public void Save(OrchestrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!IsValidId(profile.Id))
            throw new ArgumentException("An orchestration profile needs a Guid-based id.", nameof(profile));

        Normalize(profile, migrateLegacyPins: true);
        MigrateLegacyProfiles();
        EnsureDatabase();
        using var connection = Open(SqliteOpenMode.ReadWriteCreate);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO profiles(id, payload) VALUES($id, $payload) ON CONFLICT(id) DO UPDATE SET payload = excluded.payload";
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(profile, JsonOptions));
        command.ExecuteNonQuery();
        transaction.Commit();
        ArchiveLegacyProfile(profile.Id);
    }

    /// <summary>
    ///     Brings a profile into the current shape: clamps the round count, normalizes every round's four lists, and
    ///     drops entries for members that no longer exist. Called before every write and after every read, so a
    ///     hand-edited file can never leave a round pointing at a removed record.
    /// </summary>
    /// <param name="profile">Profile to normalize in place.</param>
    /// <param name="migrateLegacyPins">
    ///     Whether format-1 「第几个抽中」 pins should be folded into the round queue. Pass <c>true</c> on write,
    ///     because a written file must never carry the legacy property again.
    /// </param>
    public static void Normalize(OrchestrationProfile profile, bool migrateLegacyPins = true)
    {
        ArgumentNullException.ThrowIfNull(profile);

        profile.Rules ??= new OrchestrationRules();
        profile.Members ??= [];
        profile.Rounds ??= [];
        profile.RoundCount = Math.Clamp(profile.RoundCount, 1, OrchestrationProfile.MaxRounds);
        profile.Rounds.RemoveAll(round => round.Index is < 1 or > OrchestrationProfile.MaxRounds);
        profile.Rounds.Sort((left, right) => left.Index.CompareTo(right.Index));

        var memberIds = profile.Members.Select(member => member.RecordId).ToHashSet();
        foreach (var round in profile.Rounds)
        {
            round.Order ??= [];
            round.Skipped ??= [];
            round.Guaranteed ??= [];
            round.Weights ??= [];

            if (migrateLegacyPins && round.LegacyPins is { Count: > 0 })
                round.Order = MergeLegacyPins(round);

            round.LegacyPins = null;
            round.Order = KeepKnown(round.Order, memberIds);
            round.Skipped = KeepKnown(round.Skipped, memberIds);
            round.Guaranteed = KeepKnown(round.Guaranteed, memberIds);
            foreach (var recordId in round.Weights.Keys.Where(key => !memberIds.Contains(key)).ToList())
                round.Weights.Remove(recordId);
        }

        profile.FormatVersion = OrchestrationProfile.CurrentFormatVersion;
    }

    /// <summary>
    ///     Folds format-1 pins into the round queue: the pinned members first, in pin order, then whatever the old
    ///     order copy already held. A pinned member used to be seated at an absolute slot in the shrinking
    ///     remainder, which is exactly what the queue replaced, so the position number itself is not preserved —
    ///     only the relative order it expressed.
    /// </summary>
    private static List<Guid> MergeLegacyPins(OrchestrationRound round)
    {
        var seen = new HashSet<Guid>();
        var merged = new List<Guid>();
        foreach (var recordId in round.LegacyPins!
                     .Where(pin => pin.Position >= 1)
                     .OrderBy(pin => pin.Position)
                     .Select(pin => pin.RecordId))
            if (seen.Add(recordId))
                merged.Add(recordId);

        foreach (var recordId in round.Order)
            if (seen.Add(recordId))
                merged.Add(recordId);

        return merged;
    }

    private static List<Guid> KeepKnown(IEnumerable<Guid> recordIds, IReadOnlySet<Guid> memberIds)
    {
        var seen = new HashSet<Guid>();
        var kept = new List<Guid>();
        foreach (var recordId in recordIds)
            if (memberIds.Contains(recordId) && seen.Add(recordId))
                kept.Add(recordId);
        return kept;
    }

    public void Delete(string id)
    {
        if (!IsValidId(id))
            return;
        if (!File.Exists(DatabasePath()))
        {
            SafeDelete(LegacyProfilePath(id));
            SafeDelete(LegacyRuntimePath(id));
            return;
        }
        using var connection = Open(SqliteOpenMode.ReadWrite);
        if (!IsCurrentDatabase(connection))
            return;
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM profiles WHERE id = $id; DELETE FROM runtime WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
        transaction.Commit();
        ArchiveLegacyProfile(id);
        ArchiveLegacyRuntime(id);
    }

    public OrchestrationRuntime GetRuntime(OrchestrationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        OrchestrationRuntime? runtime = null;
        MigrateLegacyProfiles();
        if (File.Exists(DatabasePath()))
        {
            try
            {
                using var connection = Open(SqliteOpenMode.ReadOnly);
                if (IsCurrentDatabase(connection))
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT payload FROM runtime WHERE id = $id";
                    command.Parameters.AddWithValue("$id", profile.Id);
                    runtime = command.ExecuteScalar() is string payload
                        ? JsonSerializer.Deserialize<OrchestrationRuntime>(payload, JsonOptions)
                        : null;
                }
            }
            catch (JsonException)
            {
                // A damaged cursor only costs one round; the schedule itself stays valid.
                runtime = null;
            }
            catch (SqliteException)
            {
                // A damaged cursor only costs one round; the schedule itself stays valid.
                runtime = null;
            }
        }

        return new OrchestrationRuntime
        {
            ProfileId = profile.Id,
            Round = Math.Clamp(runtime?.Round ?? 1, 1, profile.EffectiveRoundCount),
            UpdatedAt = runtime?.UpdatedAt ?? default
        };
    }

    public void SaveRuntime(OrchestrationRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (!IsValidId(runtime.ProfileId))
            throw new ArgumentException("An orchestration runtime needs a Guid-based profile id.", nameof(runtime));

        runtime.UpdatedAt = DateTime.UtcNow;
        MigrateLegacyProfiles();
        EnsureDatabase();
        using var connection = Open(SqliteOpenMode.ReadWriteCreate);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO runtime(id, payload) VALUES($id, $payload) ON CONFLICT(id) DO UPDATE SET payload = excluded.payload";
        command.Parameters.AddWithValue("$id", runtime.ProfileId);
        command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(runtime, JsonOptions));
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public IReadOnlyList<OrchestrationProfile> FindActive(OrchestrationTarget target, string listName) =>
        LoadAll()
            .Where(profile => profile.IsActive
                              && profile.Target == target
                              && (string.IsNullOrWhiteSpace(listName)
                                  || string.Equals(profile.SourceListName, listName, StringComparison.Ordinal)))
            .ToList();

    public static string NewId() => Guid.NewGuid().ToString("N");

    private static string ProfileDirectory() => Utils.GetFilePath("config", "orchestration");

    private static string LegacyProfilePath(string id) =>
        Path.Combine(ProfileDirectory(), $"{id}.json");

    private static string LegacyRuntimePath(string id) =>
        Path.Combine(ProfileDirectory(), "runtime", $"{id}.json");

    internal static string DatabasePath() =>
        Utils.GetFilePath("config", $"orchestration{Utils.OrchestrationDatabaseExtension}");

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && Guid.TryParseExact(id, "N", out _);

    private static OrchestrationProfile? ReadProfilePayload(string payload)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<OrchestrationProfile>(payload, JsonOptions);
            if (profile is null)
                return null;

            if (!IsValidId(profile.Id))
                return null;
            Normalize(profile);
            return profile;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnsureDatabase()
    {
        var path = DatabasePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (File.Exists(path))
        {
            try
            {
                using var existing = Open(SqliteOpenMode.ReadOnly);
                if (IsCurrentDatabase(existing))
                    return;
            }
            catch (SqliteException)
            {
                // The file is not a database this store owns; leave it untouched.
            }

            if (File.Exists(path))
                throw new InvalidDataException($"Unexpected orchestration database format: {path}");
        }

        using var connection = Open(SqliteOpenMode.ReadWriteCreate);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
            command.CommandText = "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);" +
                              "CREATE TABLE IF NOT EXISTS profiles(id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL);" +
                              "CREATE TABLE IF NOT EXISTS runtime(id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL);" +
                              "INSERT INTO meta(key,value) VALUES('format',$format) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        command.Parameters.AddWithValue("$format", FormatMarker);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static SqliteConnection Open(SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath(),
            Mode = mode,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    internal static void MigrateLegacyProfiles()
    {
        var directory = ProfileDirectory();
        if (!Directory.Exists(directory))
            return;

        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<OrchestrationProfile>(File.ReadAllText(path), JsonOptions);
                if (profile is null)
                    continue;
                if (!IsValidId(profile.Id))
                    profile.Id = Path.GetFileNameWithoutExtension(path);
                if (!IsValidId(profile.Id))
                    continue;
                Normalize(profile);
                if (!HasProfileRow(profile.Id))
                {
                    EnsureDatabase();
                    using var connection = Open(SqliteOpenMode.ReadWriteCreate);
                    using var transaction = connection.BeginTransaction();
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = "INSERT INTO profiles(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO NOTHING";
                    command.Parameters.AddWithValue("$id", profile.Id);
                    command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(profile, JsonOptions));
                    command.ExecuteNonQuery();
                    transaction.Commit();
                }

                ArchiveLegacyProfile(profile.Id);
            }
            catch (Exception exception) when (exception is JsonException or SqliteException or IOException or InvalidDataException)
            {
                // Leave malformed legacy input in place; it may be needed for manual recovery.
            }
        }

        var runtimeDirectory = Path.Combine(directory, "runtime");
        if (!Directory.Exists(runtimeDirectory))
            return;
        foreach (var path in Directory.EnumerateFiles(runtimeDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var runtime = JsonSerializer.Deserialize<OrchestrationRuntime>(File.ReadAllText(path), JsonOptions);
                if (runtime is null)
                    continue;
                if (!IsValidId(runtime.ProfileId))
                    runtime.ProfileId = Path.GetFileNameWithoutExtension(path);
                if (!IsValidId(runtime.ProfileId))
                    continue;
                EnsureDatabase();
                using var connection = Open(SqliteOpenMode.ReadWriteCreate);
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO runtime(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO NOTHING";
                command.Parameters.AddWithValue("$id", runtime.ProfileId);
                command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(runtime, JsonOptions));
                command.ExecuteNonQuery();
                transaction.Commit();
                ArchiveJson(path);
            }
            catch (Exception exception) when (exception is JsonException or SqliteException or IOException or InvalidDataException)
            {
                // Leave malformed legacy input in place; it may be needed for manual recovery.
            }
        }
    }

    private static bool HasProfileRow(string id)
    {
        if (!File.Exists(DatabasePath()))
            return false;
        try
        {
            using var connection = Open(SqliteOpenMode.ReadOnly);
            if (!IsCurrentDatabase(connection))
                return false;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM profiles WHERE id = $id";
            command.Parameters.AddWithValue("$id", id);
            return command.ExecuteScalar() is not null;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void ArchiveLegacyProfile(string id)
    {
        ArchiveJson(LegacyProfilePath(id));
    }

    private static void ArchiveLegacyRuntime(string id)
    {
        ArchiveJson(LegacyRuntimePath(id));
    }

    private static void ArchiveJson(string path)
    {
        if (!File.Exists(path))
            return;
        var backup = path + ".migrated";
        if (!File.Exists(backup))
        {
            File.Move(path, backup);
            return;
        }

        if (FilesEqual(path, backup))
        {
            File.Delete(path);
            return;
        }

        for (var index = 1; ; index++)
        {
            var alternate = $"{backup}.{index}";
            if (!File.Exists(alternate))
            {
                File.Move(path, alternate);
                return;
            }
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var buffer = new byte[81920];
        var compare = new byte[buffer.Length];
        int read;
        while ((read = leftStream.Read(buffer, 0, buffer.Length)) > 0)
            if (rightStream.Read(compare, 0, read) != read || !buffer.AsSpan(0, read).SequenceEqual(compare.AsSpan(0, read)))
                return false;

        return rightStream.ReadByte() == -1;
    }

    private static bool IsCurrentDatabase(SqliteConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT value FROM meta WHERE key = 'format'";
            return string.Equals(command.ExecuteScalar() as string, FormatMarker, StringComparison.Ordinal);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover file is harmless: the next save overwrites its database row.
        }
    }
}
