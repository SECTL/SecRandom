using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Core.Helpers;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
/// SQLite persistence for all rosters of one draw kind. A database contains many named
/// profiles; the profile name is a row key rather than part of the file name.
/// </summary>
internal static class ProfileRosterDatabase
{
    private const int SchemaVersion = 2;
    private const string FormatMarker = "SecRandomRosterDatabase/v2";
    private const string LegacyFormatMarker = "SecRandomRosterDatabase/v1";

    internal static StudentList LoadStudentList(string profileName, ILogger logger)
    {
        var list = new StudentList(profileName);
        var snapshot = LoadSnapshot(profileName, ProfileRosterKind.RollCall, logger);
        if (snapshot is null)
            return list;

        foreach (var row in snapshot.Members)
            list.Students.Add(ReadStudent(row));
        list.AttachedObjects = new Dictionary<Guid, object?>(snapshot.AttachedObjects);
        if (ProfileRecordIdentity.Normalize(list))
            SaveStudentList(list, logger);
        return list;
    }

    internal static PrizeList LoadPrizeList(string profileName, ILogger logger)
    {
        var list = new PrizeList(profileName);
        var snapshot = LoadSnapshot(profileName, ProfileRosterKind.Lottery, logger);
        if (snapshot is null)
            return list;

        foreach (var row in snapshot.Members)
            list.Prizes.Add(ReadPrize(row));
        list.AttachedObjects = new Dictionary<Guid, object?>(snapshot.AttachedObjects);
        if (ProfileRecordIdentity.Normalize(list))
            SavePrizeList(list, logger);
        return list;
    }

    internal static void SaveStudentList(StudentList list, ILogger logger)
    {
        ProfileRecordIdentity.Normalize(list);
        WriteSnapshot(list.Name, ProfileRosterKind.RollCall,
            list.Students.Select(WriteStudent).ToArray(), list.AttachedObjects, logger);
    }

    internal static void SavePrizeList(PrizeList list, ILogger logger)
    {
        ProfileRecordIdentity.Normalize(list);
        WriteSnapshot(list.Name, ProfileRosterKind.Lottery,
            list.Prizes.Select(WritePrize).ToArray(), list.AttachedObjects, logger);
    }

    internal static bool Exists(string profileName, ProfileRosterKind kind, ILogger logger)
    {
        ProfileConfigNameIsValid(profileName);
        return GetProfileNames(kind, logger).Contains(profileName, StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> GetProfileNames(ProfileRosterKind kind, ILogger logger)
    {
        MigrateLegacyProfiles(kind, logger);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        if (File.Exists(databasePath))
        {
            try
            {
                if (!HasSqliteHeader(databasePath))
                    throw new InvalidDataException($"Unexpected roster database header: {databasePath}");
                using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
                if (IsCurrentDatabase(connection, kind))
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT profile_name FROM profiles";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        var name = reader.GetString(0);
                        if (ProfileConfigNameIsValid(name))
                            names.Add(name);
                    }
                }
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "读取名单数据库目录失败：{Path}", databasePath);
            }
        }

        foreach (var name in ProfileRosterPaths.EnumerateLegacyProfileNames(kind))
            names.Add(name);

        return names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    internal static void Delete(string profileName, ProfileRosterKind kind, ILogger logger)
    {
        ProfileConfigNameIsValid(profileName);
        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        if (File.Exists(databasePath))
        {
            if (!HasSqliteHeader(databasePath))
                throw new InvalidDataException($"Unexpected roster database header for {profileName} ({kind}).");
            if (!IsCurrentDatabaseFile(databasePath, kind))
                throw new InvalidDataException($"Unexpected roster database for {profileName} ({kind}).");
            using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction, "DELETE FROM members WHERE profile_name = $name", ("$name", profileName));
            Execute(connection, transaction, "DELETE FROM attached_objects WHERE profile_name = $name", ("$name", profileName));
            Execute(connection, transaction, "DELETE FROM profiles WHERE profile_name = $name", ("$name", profileName));
            transaction.Commit();
        }

        ProfileRosterPaths.DeleteLegacy(profileName, kind);
    }

    internal static void Rename(string oldName, string newName, ProfileRosterKind kind, ILogger logger)
    {
        ProfileConfigNameIsValid(oldName);
        ProfileConfigNameIsValid(newName);
        MigrateLegacyProfile(oldName, kind, logger);

        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        if (File.Exists(databasePath))
        {
            if (!HasSqliteHeader(databasePath))
                throw new InvalidDataException($"Unexpected roster database header for {oldName} ({kind}).");
            if (!IsCurrentDatabaseFile(databasePath, kind))
                throw new InvalidDataException($"Unexpected roster database for {oldName} ({kind}).");
            using var connection = Open(databasePath, SqliteOpenMode.ReadWrite);
            using var transaction = connection.BeginTransaction();
            Execute(connection, transaction,
                "UPDATE profiles SET profile_name = $newName WHERE profile_name = $oldName",
                ("$newName", newName), ("$oldName", oldName));
            Execute(connection, transaction,
                "UPDATE members SET profile_name = $newName WHERE profile_name = $oldName",
                ("$newName", newName), ("$oldName", oldName));
            Execute(connection, transaction,
                "UPDATE attached_objects SET profile_name = $newName WHERE profile_name = $oldName",
                ("$newName", newName), ("$oldName", oldName));
            transaction.Commit();
        }

        ProfileRosterPaths.RenameLegacy(oldName, newName, kind);
    }

    /// <summary>Imports all legacy per-profile JSON/SQLite files into the central store.</summary>
    internal static void MigrateLegacyProfiles(ProfileRosterKind kind, ILogger logger)
    {
        foreach (var name in ProfileRosterPaths.EnumerateLegacyProfileNames(kind))
        {
            try
            {
                MigrateLegacyProfile(name, kind, logger);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "名单迁移失败，保留旧文件：{ProfileName} ({Kind})", name, kind);
            }
        }
    }

    /// <summary>Compatibility entry point used after archive import.</summary>
    internal static void MigrateLegacyJsonIfPresent(string profileName, ProfileRosterKind kind, ILogger logger) =>
        MigrateLegacyProfile(profileName, kind, logger);

    private static void MigrateLegacyProfile(string profileName, ProfileRosterKind kind, ILogger logger)
    {
        ProfileConfigNameIsValid(profileName);
        if (!ProfileRosterPaths.HasLegacyFiles(profileName, kind))
            return;

        if (HasProfile(profileName, kind))
        {
            CompleteLegacyMigration(profileName, kind);
            return;
        }

        RosterSnapshot? snapshot = null;
        var legacyDatabase = ProfileRosterPaths.LegacyDatabasePath(profileName, kind);
        var legacyJson = ProfileRosterPaths.LegacyJsonPath(profileName, kind);
        if (File.Exists(legacyDatabase))
        {
            try
            {
                snapshot = ReadLegacyDatabase(legacyDatabase, profileName, kind);
            }
            catch (Exception exception) when (exception is SqliteException or InvalidDataException or IOException)
            {
                if (!File.Exists(legacyJson))
                    throw;
                logger.LogWarning(exception, "旧版名单数据库不可读，尝试同名 JSON 迁移并保留 SQLite 原件：{ProfileName} ({Kind})", profileName, kind);
            }
        }
        else if (File.Exists(legacyJson))
            snapshot = ReadLegacyJson(legacyJson, profileName, kind);

        if (snapshot is null && File.Exists(legacyJson))
            snapshot = ReadLegacyJson(legacyJson, profileName, kind);

        if (snapshot is null)
            return;

        WriteSnapshot(profileName, kind, snapshot.Members, snapshot.AttachedObjects, logger);
        CompleteLegacyMigration(profileName, kind);
        logger.LogInformation("已把名单档案迁移到集中数据库：档案={ProfileName}，类型={Kind}。", profileName, kind);
    }

    private static RosterSnapshot? LoadSnapshot(string profileName, ProfileRosterKind kind, ILogger logger)
    {
        ProfileConfigNameIsValid(profileName);
        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        if (File.Exists(databasePath))
        {
            try
            {
                if (!HasSqliteHeader(databasePath))
                    throw new InvalidDataException($"Unexpected roster database header: {databasePath}");
                var snapshot = ReadSnapshot(databasePath, profileName, kind);
                if (snapshot is not null)
                    return snapshot;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "读取名单数据库失败，本次按空名单处理：{Path}", databasePath);
                return null;
            }
        }

        try
        {
            MigrateLegacyProfile(profileName, kind, logger);
        }
        catch (Exception exception)
        {
            // A damaged legacy database must not prevent the application from starting. Keep it in place for
            // recovery and expose an empty in-memory list until the user imports or repairs it.
            logger.LogWarning(exception, "读取旧版名单失败，本次按空名单处理并保留原文件：{ProfileName} ({Kind})", profileName, kind);
            return null;
        }
        if (!File.Exists(databasePath))
            return null;

        try
        {
            return ReadSnapshot(databasePath, profileName, kind);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "读取迁移后的名单数据库失败，本次按空名单处理：{Path}", databasePath);
            return null;
        }
    }

    private static RosterSnapshot? ReadLegacyJson(string path, string profileName, ProfileRosterKind kind)
    {
        var json = SharedFileReader.ReadAllText(path);
        if (kind == ProfileRosterKind.RollCall)
        {
            var list = JsonSerializer.Deserialize<StudentList>(json, ConfigServiceBase.JsonOptions);
            if (list is null)
                return null;
            ProfileRecordIdentity.Normalize(list);
            return new RosterSnapshot(list.Students.Select(WriteStudent).ToArray(), list.AttachedObjects);
        }

        var prizeList = JsonSerializer.Deserialize<PrizeList>(json, ConfigServiceBase.JsonOptions);
        if (prizeList is null)
            return null;
        ProfileRecordIdentity.Normalize(prizeList);
        return new RosterSnapshot(prizeList.Prizes.Select(WritePrize).ToArray(), prizeList.AttachedObjects);
    }

    private static RosterSnapshot? ReadLegacyDatabase(string path, string profileName, ProfileRosterKind kind)
    {
        if (!HasSqliteHeader(path))
            throw new InvalidDataException($"Unexpected legacy roster database header for {profileName} ({kind}).");

        using var connection = Open(path, SqliteOpenMode.ReadOnly);
        var meta = ReadMeta(connection);
        if (!string.Equals(meta.GetValueOrDefault("format"), LegacyFormatMarker, StringComparison.Ordinal) ||
            !string.Equals(meta.GetValueOrDefault("schema_version"), "1", StringComparison.Ordinal) ||
            !string.Equals(meta.GetValueOrDefault("profile_name"), profileName, StringComparison.Ordinal) ||
            !string.Equals(meta.GetValueOrDefault("kind"), kind.ToString(), StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected legacy roster database for {profileName} ({kind}).");

        var members = new List<MemberRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT record_id, [exists], id, name, gender, group_name, tags, count, weight, payload FROM members ORDER BY ordinal";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                members.Add(new MemberRow(reader.GetString(0), reader.GetInt64(1) != 0, reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), (int)reader.GetInt64(7), reader.GetDouble(8), reader.GetString(9)));
        }

        var attached = new Dictionary<Guid, object?>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT control_id, payload FROM attached_objects";
            using var reader = command.ExecuteReader();
            while (reader.Read() && Guid.TryParse(reader.GetString(0), out var id))
                attached[id] = DeserializeAttached(reader.GetString(1));
        }

        return new RosterSnapshot(members, attached);
    }

    private static RosterSnapshot? ReadSnapshot(string databasePath, string profileName, ProfileRosterKind kind)
    {
        using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
        if (!IsCurrentDatabase(connection, kind))
            throw new InvalidDataException($"Unexpected roster database for {profileName} ({kind}).");

        using var profile = connection.CreateCommand();
        profile.CommandText = "SELECT 1 FROM profiles WHERE profile_name = $name";
        profile.Parameters.AddWithValue("$name", profileName);
        if (profile.ExecuteScalar() is null)
            return null;

        var members = new List<MemberRow>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT record_id, [exists], id, name, gender, group_name, tags, count, weight, payload FROM members WHERE profile_name = $name ORDER BY ordinal";
            command.Parameters.AddWithValue("$name", profileName);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                members.Add(new MemberRow(reader.GetString(0), reader.GetInt64(1) != 0, reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), (int)reader.GetInt64(7), reader.GetDouble(8), reader.GetString(9)));
        }

        var attached = new Dictionary<Guid, object?>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT control_id, payload FROM attached_objects WHERE profile_name = $name";
            command.Parameters.AddWithValue("$name", profileName);
            using var reader = command.ExecuteReader();
            while (reader.Read() && Guid.TryParse(reader.GetString(0), out var id))
                attached[id] = DeserializeAttached(reader.GetString(1));
        }

        return new RosterSnapshot(members, attached);
    }

    private static void WriteSnapshot(string profileName, ProfileRosterKind kind, IReadOnlyList<MemberRow> members,
        IReadOnlyDictionary<Guid, object?> attachedObjects, ILogger logger)
    {
        ProfileConfigNameIsValid(profileName);
        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        logger.LogInformation("Saving centralized roster database: {Path} ({ProfileName})", databasePath, profileName);
        var databaseExists = File.Exists(databasePath);
        if (databaseExists && !HasSqliteHeader(databasePath))
            throw new InvalidDataException($"Refusing to overwrite a non-SQLite roster database: {databasePath}");
        if (databaseExists && !IsCurrentDatabaseFile(databasePath, kind))
            throw new InvalidDataException($"Refusing to overwrite an unrecognized roster database: {databasePath}");
        using var connection = Open(databasePath, databaseExists ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadWriteCreate);
        using var transaction = connection.BeginTransaction();
        EnsureSchema(connection, transaction, kind);

        Execute(connection, transaction, "INSERT INTO profiles(profile_name) VALUES($name) ON CONFLICT(profile_name) DO NOTHING", ("$name", profileName));
        Execute(connection, transaction, "DELETE FROM members WHERE profile_name = $name", ("$name", profileName));
        Execute(connection, transaction, "DELETE FROM attached_objects WHERE profile_name = $name", ("$name", profileName));

        using (var member = connection.CreateCommand())
        {
            member.Transaction = transaction;
            member.CommandText = "INSERT INTO members(profile_name, ordinal, record_id, [exists], id, name, gender, group_name, tags, count, weight, payload) VALUES($profile, $ordinal, $recordId, $exists, $id, $name, $gender, $groupName, $tags, $count, $weight, $payload)";
            var profile = member.Parameters.Add("$profile", SqliteType.Text);
            var ordinal = member.Parameters.Add("$ordinal", SqliteType.Integer);
            var recordId = member.Parameters.Add("$recordId", SqliteType.Text);
            var exists = member.Parameters.Add("$exists", SqliteType.Integer);
            var id = member.Parameters.Add("$id", SqliteType.Text);
            var name = member.Parameters.Add("$name", SqliteType.Text);
            var gender = member.Parameters.Add("$gender", SqliteType.Text);
            var groupName = member.Parameters.Add("$groupName", SqliteType.Text);
            var tags = member.Parameters.Add("$tags", SqliteType.Text);
            var count = member.Parameters.Add("$count", SqliteType.Integer);
            var weight = member.Parameters.Add("$weight", SqliteType.Real);
            var payload = member.Parameters.Add("$payload", SqliteType.Text);
            foreach (var (row, index) in members.Select((row, index) => (row, index)))
            {
                profile.Value = profileName; ordinal.Value = index; recordId.Value = row.RecordId; exists.Value = row.Exists ? 1 : 0;
                id.Value = row.Id; name.Value = row.Name; gender.Value = row.Gender; groupName.Value = row.Group;
                tags.Value = row.Tags; count.Value = row.Count; weight.Value = row.Weight; payload.Value = row.Payload;
                member.ExecuteNonQuery();
            }
        }

        using (var attached = connection.CreateCommand())
        {
            attached.Transaction = transaction;
            attached.CommandText = "INSERT INTO attached_objects(profile_name, control_id, payload) VALUES($profile, $controlId, $payload)";
            var profile = attached.Parameters.Add("$profile", SqliteType.Text);
            var controlId = attached.Parameters.Add("$controlId", SqliteType.Text);
            var payload = attached.Parameters.Add("$payload", SqliteType.Text);
            foreach (var (controlIdValue, value) in attachedObjects)
            {
                profile.Value = profileName; controlId.Value = controlIdValue.ToString("D");
                payload.Value = JsonSerializer.Serialize(value, ConfigServiceBase.JsonOptions);
                attached.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private static bool HasProfile(string profileName, ProfileRosterKind kind)
    {
        var databasePath = ProfileRosterPaths.DatabasePath(kind);
        if (!File.Exists(databasePath))
            return false;
        try
        {
            if (!HasSqliteHeader(databasePath))
                return false;
            using var connection = Open(databasePath, SqliteOpenMode.ReadOnly);
            if (!IsCurrentDatabase(connection, kind))
                return false;
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1 FROM profiles WHERE profile_name = $name";
            command.Parameters.AddWithValue("$name", profileName);
            return command.ExecuteScalar() is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCurrentDatabase(SqliteConnection connection, ProfileRosterKind kind)
    {
        var meta = ReadMeta(connection);
        return string.Equals(meta.GetValueOrDefault("format"), FormatMarker, StringComparison.Ordinal) &&
               string.Equals(meta.GetValueOrDefault("schema_version"), SchemaVersion.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) &&
               string.Equals(meta.GetValueOrDefault("kind"), kind.ToString(), StringComparison.Ordinal);
    }

    private static bool IsCurrentDatabaseFile(string path, ProfileRosterKind kind)
    {
        try
        {
            using var connection = Open(path, SqliteOpenMode.ReadOnly);
            return IsCurrentDatabase(connection, kind);
        }
        catch (Exception exception) when (exception is SqliteException or InvalidDataException or IOException)
        {
            return false;
        }
    }

    private static bool HasSqliteHeader(string path)
    {
        Span<byte> header = stackalloc byte[16];
        using var stream = File.OpenRead(path);
        return stream.Read(header) == header.Length && header.SequenceEqual("SQLite format 3\0"u8);
    }

    private static Dictionary<string, string> ReadMeta(SqliteConnection connection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private static void EnsureSchema(SqliteConnection connection, SqliteTransaction transaction, ProfileRosterKind kind)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);" +
                              "CREATE TABLE IF NOT EXISTS profiles(profile_name TEXT PRIMARY KEY NOT NULL);" +
                              "CREATE TABLE IF NOT EXISTS members(profile_name TEXT NOT NULL, ordinal INTEGER NOT NULL, record_id TEXT NOT NULL, [exists] INTEGER NOT NULL, id TEXT NOT NULL, name TEXT NOT NULL, gender TEXT NOT NULL, group_name TEXT NOT NULL, tags TEXT NOT NULL, count INTEGER NOT NULL, weight REAL NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(profile_name, ordinal));" +
                              "CREATE TABLE IF NOT EXISTS attached_objects(profile_name TEXT NOT NULL, control_id TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(profile_name, control_id));";
        command.ExecuteNonQuery();

        Execute(connection, transaction, "INSERT INTO meta(key,value) VALUES('format',$format) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$format", FormatMarker));
        Execute(connection, transaction, "INSERT INTO meta(key,value) VALUES('schema_version',$version) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$version", SchemaVersion.ToString(CultureInfo.InvariantCulture)));
        Execute(connection, transaction, "INSERT INTO meta(key,value) VALUES('kind',$kind) ON CONFLICT(key) DO UPDATE SET value=excluded.value", ("$kind", kind.ToString()));
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void CompleteLegacyMigration(string profileName, ProfileRosterKind kind)
    {
        var legacyDatabase = ProfileRosterPaths.LegacyDatabasePath(profileName, kind);
        var databaseBackup = ProfileRosterPaths.LegacyDatabaseBackupPath(profileName, kind);
        MoveToBackup(ProfileRosterPaths.LegacyJsonPath(profileName, kind), ProfileRosterPaths.LegacyBackupPath(profileName, kind));
        var actualDatabaseBackup = MoveToBackup(legacyDatabase, databaseBackup) ?? databaseBackup;
        MoveToBackup(legacyDatabase + "-wal", actualDatabaseBackup + "-wal");
        MoveToBackup(legacyDatabase + "-shm", actualDatabaseBackup + "-shm");
    }

    private static string? MoveToBackup(string source, string backup)
    {
        if (!File.Exists(source))
            return null;

        var destination = backup;
        if (File.Exists(backup))
        {
            if (FilesEqual(source, backup))
            {
                File.Delete(source);
                return backup;
            }

            for (var index = 1; ; index++)
            {
                var alternate = $"{backup}.{index}";
                if (!File.Exists(alternate))
                {
                    destination = alternate;
                    break;
                }
            }
        }

        File.Move(source, destination);
        return destination;
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        var leftBuffer = new byte[81920];
        var rightBuffer = new byte[leftBuffer.Length];
        int leftRead;
        while ((leftRead = leftStream.Read(leftBuffer, 0, leftBuffer.Length)) > 0)
        {
            if (rightStream.Read(rightBuffer, 0, leftRead) != leftRead ||
                !leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, leftRead)))
                return false;
        }

        return rightStream.ReadByte() == -1;
    }

    private static MemberRow WriteStudent(Student student) => new(student.RecordId.ToString("D"), student.Exists, student.Id, student.Name,
        student.Gender, student.Group, student.Tags, 1, 1, SerializeAttachedObjects(student.AttachedObjects));

    private static MemberRow WritePrize(Prize prize) => new(prize.RecordId.ToString("D"), prize.Exists, prize.Id, prize.Name,
        string.Empty, string.Empty, prize.Tags, prize.Count, prize.Weight, SerializeAttachedObjects(prize.AttachedObjects));

    private static Student ReadStudent(MemberRow row)
    {
        var student = new Student { RecordId = ParseGuid(row.RecordId), Exists = row.Exists, Id = row.Id, Name = row.Name,
            Gender = row.Gender, Group = row.Group, Tags = row.Tags };
        RestoreAttachedObjects(student.AttachedObjects, row.Payload);
        return student;
    }

    private static Prize ReadPrize(MemberRow row)
    {
        var prize = new Prize { RecordId = ParseGuid(row.RecordId), Exists = row.Exists, Id = row.Id, Name = row.Name,
            Tags = row.Tags, Count = row.Count, Weight = row.Weight };
        RestoreAttachedObjects(prize.AttachedObjects, row.Payload);
        return prize;
    }

    private static Guid ParseGuid(string value) => Guid.TryParse(value, out var result) ? result : Guid.Empty;

    private static string SerializeAttachedObjects(IReadOnlyDictionary<Guid, object?> values) =>
        JsonSerializer.Serialize(values, ConfigServiceBase.JsonOptions);

    private static object? DeserializeAttached(string payload)
    {
        try { return JsonSerializer.Deserialize<object>(payload, ConfigServiceBase.JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static void RestoreAttachedObjects(Dictionary<Guid, object?> target, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return;
        try
        {
            var values = JsonSerializer.Deserialize<Dictionary<Guid, object?>>(payload, ConfigServiceBase.JsonOptions);
            if (values is not null)
                foreach (var (controlId, value) in values)
                    target[controlId] = value;
        }
        catch (JsonException) { }
    }

    private static bool ProfileConfigNameIsValid(string name)
    {
        SecRandom.Shared.Abstraction.ProfileConfigBase.ValidateProfileName(name);
        return true;
    }

    private sealed record RosterSnapshot(IReadOnlyList<MemberRow> Members, IReadOnlyDictionary<Guid, object?> AttachedObjects);
    private sealed record MemberRow(string RecordId, bool Exists, string Id, string Name, string Gender, string Group,
        string Tags, int Count, double Weight, string Payload);
}
