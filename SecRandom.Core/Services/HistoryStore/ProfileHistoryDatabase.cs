using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.HistoryStore;

/// <summary>
///     <c>data/history</c> 里一个档案的读写实现：历史记录不再存成一个 JSON 文件，而是存成一个小的 SQLite 数据库。
/// </summary>
/// <remarks>
///     <para>
///         动机是读写放大，而不是存储格式本身的洁癖：旧实现的每一次保存都会把整份历史重新序列化并整体替换文件，而一次抽取
///         会连续触发三次保存（两个累计字段各一次、提交末尾一次），于是历史越长，一次抽取付出的写入成本越高；读取侧同样
///         会为列出最近记录把每个档案完整反序列化一遍。
///     </para>
///     <para>
///         迁移是无感的：第一次读取某个档案时，如果数据库还不存在而旧的 <c>{name}.json</c> 在，就先按旧格式读出来、整份写入
///         数据库，再把旧文件改名成 <c>{name}.json.migrated</c> 留作一次性回滚副本。改名后扩展名不再匹配 <c>*.json</c>，
///         所以归档校验、旧快照读取这些 JSON 入口都不会把它当成档案；换句话说迁移只发生一次，且由第一次真实读取触发，
///         不需要用户在设置里点任何东西。
///     </para>
///     <para>
///         保存是增量的：每行记录都带一个内容指纹（<c>row_hash</c>），保存时先读出磁盘上的指纹表，只为新增、修改和删除的
///         行发语句。新增一条抽取记录因此只写"那个成员 + 它的新记录"，而不是整份历史；只有组改名这类一次性重写全部记录的
///         操作才会接近整份写入的成本。相对 JSON 仍然严格更好：崩溃时事务回滚，旧内容保持完整。
///     </para>
/// </remarks>
internal static class ProfileHistoryDatabase
{
    private const int SchemaVersion = 2;
    private const string FormatMarker = "SecRandomHistoryDatabase/v2";

    /// <summary>读取点名历史；没有数据库也没有旧 JSON 时返回空档案（不落盘，等第一次保存再建库）。</summary>
    internal static StudentHistory LoadStudentHistory(string profileName, ILogger logger)
    {
        var snapshot = LoadSnapshot(profileName, HistoryGroupKind.RollCall, logger);
        var history = new StudentHistory(profileName);
        if (snapshot is null)
            return history;

        history.TotalRounds = snapshot.TotalRounds;
        history.TotalStats = snapshot.TotalStats;
        foreach (var pair in snapshot.Members)
            history.Students[pair.Key] = pair.Value;
        foreach (var pair in snapshot.GroupStats)
            history.GroupStats[pair.Key] = pair.Value;
        foreach (var pair in snapshot.GenderStatus)
            history.GenderStatus[pair.Key] = pair.Value;
        foreach (var pair in snapshot.Groups)
            history.HistoryGroups[pair.Key] = pair.Value;
        return history;
    }

    /// <summary>读取抽奖历史；语义同 <see cref="LoadStudentHistory" />。</summary>
    internal static PrizeHistory LoadPrizeHistory(string profileName, ILogger logger)
    {
        var snapshot = LoadSnapshot(profileName, HistoryGroupKind.Lottery, logger);
        var history = new PrizeHistory(profileName);
        if (snapshot is null)
            return history;

        history.TotalRounds = snapshot.TotalRounds;
        history.TotalStats = snapshot.TotalStats;
        foreach (var pair in snapshot.Members)
            history.Prizes[pair.Key] = pair.Value;
        foreach (var pair in snapshot.GroupStats)
            history.GroupStats[pair.Key] = pair.Value;
        foreach (var pair in snapshot.GenderStatus)
            history.GenderStatus[pair.Key] = pair.Value;
        foreach (var pair in snapshot.Groups)
            history.HistoryGroups[pair.Key] = pair.Value;
        return history;
    }

    internal static void SaveStudentHistory(StudentHistory history, ILogger logger)
    {
        WriteSnapshot(history.Name, HistoryGroupKind.RollCall, SnapshotOf(history), logger);
    }

    internal static void SavePrizeHistory(PrizeHistory history, ILogger logger)
    {
        WriteSnapshot(history.Name, HistoryGroupKind.Lottery, SnapshotOf(history), logger);
    }

    /// <summary>删除档案：数据库、可能残留的旧 JSON 与迁移备份一起删掉。</summary>
    internal static void Delete(string profileName, HistoryGroupKind kind, ILogger logger)
    {
        logger.LogInformation("Deleting history profile: {ProfileName} ({Kind})", profileName, kind);
        ProfileHistoryPaths.Delete(profileName, kind);
    }

    private static HistorySnapshot? LoadSnapshot(string profileName, HistoryGroupKind kind, ILogger logger)
    {
        var databasePath = ProfileHistoryPaths.DatabasePath(profileName, kind);
        if (!File.Exists(databasePath))
        {
            var legacyPath = ProfileHistoryPaths.LegacyJsonPath(profileName, kind);
            return File.Exists(legacyPath)
                ? ImportLegacyJson(profileName, kind, legacyPath, databasePath, logger)
                : null;
        }

        try
        {
            return ReadSnapshot(databasePath, logger);
        }
        catch (Exception exception)
        {
            // 库损坏或不是本程序写的：保持文件原样，本次按空历史处理，下一次保存会重建成完整结构。
            logger.LogWarning(exception, "读取历史记录数据库失败，本次按空历史处理：{Path}", databasePath);
            return null;
        }
    }

    private static HistorySnapshot? ImportLegacyJson(
        string profileName,
        HistoryGroupKind kind,
        string legacyPath,
        string databasePath,
        ILogger logger)
    {
        try
        {
            var json = File.ReadAllText(legacyPath);
            HistorySnapshot snapshot;
            if (kind == HistoryGroupKind.RollCall)
            {
                var legacy = JsonSerializer.Deserialize<StudentHistory>(json, ConfigServiceBase.JsonOptions);
                if (legacy is null)
                    return null;

                snapshot = SnapshotOf(legacy);
            }
            else
            {
                var legacy = JsonSerializer.Deserialize<PrizeHistory>(json, ConfigServiceBase.JsonOptions);
                if (legacy is null)
                    return null;

                snapshot = SnapshotOf(legacy);
            }

            WriteSnapshot(profileName, kind, snapshot, logger);

            // 迁移成功才改名：否则宁可保留 JSON 让下次启动再试，也不要把唯一一份历史弄丢。
            File.Move(legacyPath, ProfileHistoryPaths.LegacyBackupPath(profileName, kind), overwrite: true);
            logger.LogInformation("已把历史记录 JSON 迁移到数据库：档案={ProfileName}，类型={Kind}。", profileName, kind);
            return snapshot;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "历史记录 JSON 迁移失败，保留原文件：{Path}", legacyPath);
            return null;
        }
    }

    private static void WriteSnapshot(
        string profileName,
        HistoryGroupKind kind,
        HistorySnapshot snapshot,
        ILogger logger)
    {
        var databasePath = ProfileHistoryPaths.DatabasePath(profileName, kind);
        logger.LogInformation("Saving history database: {Path}", databasePath);

        using var connection = Open(databasePath);
        using var transaction = connection.BeginTransaction();
        EnsureSchema(connection, transaction);

        // 累计字段、组登记与统计表都很小，每次都整体重写；记录行才是随历史线性增长的那部分，走下面的增量逻辑。
        WriteMeta(connection, transaction, profileName, kind, snapshot);
        WriteGroups(connection, transaction, snapshot);
        WriteCounters(connection, transaction, snapshot);

        var stored = ReadStoredRows(connection, transaction);
        var currentKeys = new HashSet<string>(StringComparer.Ordinal);
        using var memberCommand = CreateMemberUpsertCommand(connection, transaction);
        using var insertCommand = CreateRecordInsertCommand(connection, transaction);
        using var updateCommand = CreateRecordUpdateCommand(connection, transaction);

        foreach (var (key, member) in snapshot.Members)
        {
            currentKeys.Add(key);
            UpsertMember(memberCommand, key, member);

            var hashes = new long[member.Histories.Count];
            for (var index = 0; index < hashes.Length; index++)
                hashes[index] = ComputeRowHash(member.Histories[index]);

            if (!stored.TryGetValue(key, out var previous))
            {
                // 新成员：整段插入。
                InsertRecords(insertCommand, key, member.Histories, hashes, 0);
                continue;
            }

            if (previous.Count != hashes.Length)
            {
                // 记录条数变了（新增/删除/清空）：按成员重写，成本上限是该成员的记录数而不是整份历史。
                DeleteMemberRecords(connection, transaction, key);
                InsertRecords(insertCommand, key, member.Histories, hashes, 0);
                continue;
            }

            // 条数没变：只更新内容真的变了的行（组改名/移组等），其余一行不动。
            for (var index = 0; index < hashes.Length; index++)
            {
                if (previous[index] == hashes[index])
                    continue;

                UpdateRecord(updateCommand, key, index, member.Histories[index], hashes[index]);
            }
        }

        foreach (var key in stored.Keys)
        {
            if (!currentKeys.Contains(key))
                DeleteMemberRecords(connection, transaction, key);
        }

        transaction.Commit();
    }

    private static void WriteMeta(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string profileName,
        HistoryGroupKind kind,
        HistorySnapshot snapshot)
    {
        Execute(connection, transaction, "DELETE FROM meta");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO meta (key, value) VALUES ($key, $value)";
        var keyParameter = command.Parameters.Add("$key", SqliteType.Text);
        var valueParameter = command.Parameters.Add("$value", SqliteType.Text);
        foreach (var pair in new (string Key, string Value)[]
                 {
                     ("format", FormatMarker),
                     ("schema_version", SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                     ("profile_name", profileName),
                     ("kind", kind.ToString()),
                     ("total_rounds", snapshot.TotalRounds.ToString(CultureInfo.InvariantCulture)),
                     ("total_stats", snapshot.TotalStats.ToString(CultureInfo.InvariantCulture))
                 })
        {
            keyParameter.Value = pair.Key;
            valueParameter.Value = pair.Value;
            command.ExecuteNonQuery();
        }
    }

    private static void WriteGroups(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistorySnapshot snapshot)
    {
        Execute(connection, transaction, "DELETE FROM groups");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO groups (name, created_at, last_used_at) VALUES ($name, $created, $lastUsed)";
        var nameParameter = command.Parameters.Add("$name", SqliteType.Text);
        var createdParameter = command.Parameters.Add("$created", SqliteType.Text);
        var lastUsedParameter = command.Parameters.Add("$lastUsed", SqliteType.Text);
        foreach (var (name, info) in snapshot.Groups)
        {
            nameParameter.Value = name;
            createdParameter.Value = FormatTimestamp(info.CreatedAt);
            lastUsedParameter.Value = FormatTimestamp(info.LastUsedAt);
            command.ExecuteNonQuery();
        }
    }

    private static void WriteCounters(
        SqliteConnection connection,
        SqliteTransaction transaction,
        HistorySnapshot snapshot)
    {
        Execute(connection, transaction, "DELETE FROM counters");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO counters (kind, name, value) VALUES ($kind, $name, $value)";
        var kindParameter = command.Parameters.Add("$kind", SqliteType.Text);
        var nameParameter = command.Parameters.Add("$name", SqliteType.Text);
        var valueParameter = command.Parameters.Add("$value", SqliteType.Integer);
        foreach (var (counterKind, counters) in new (string Kind, IEnumerable<KeyValuePair<string, int>> Values)[]
                 {
                     ("group_stat", snapshot.GroupStats),
                     ("gender_status", snapshot.GenderStatus)
                 })
        {
            kindParameter.Value = counterKind;
            foreach (var (name, value) in counters)
            {
                nameParameter.Value = name;
                valueParameter.Value = value;
                command.ExecuteNonQuery();
            }
        }
    }

    /// <summary>读出磁盘上每个成员已有的记录指纹，用来判断哪些行需要写。</summary>
    private static Dictionary<string, List<long>> ReadStoredRows(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var stored = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT member_key, row_hash FROM records ORDER BY member_key, ordinal";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            if (!stored.TryGetValue(key, out var rows))
            {
                rows = [];
                stored[key] = rows;
            }

            rows.Add(reader.GetInt64(1));
        }

        return stored;
    }

    private static SqliteCommand CreateMemberUpsertCommand(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO members (member_key, rounds_missed, total_count, last_drawn_at) " +
            "VALUES ($key, $missed, $count, $lastDrawn) " +
            "ON CONFLICT (member_key) DO UPDATE SET rounds_missed = excluded.rounds_missed, " +
            "total_count = excluded.total_count, last_drawn_at = excluded.last_drawn_at";
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$missed", SqliteType.Integer);
        command.Parameters.Add("$count", SqliteType.Integer);
        command.Parameters.Add("$lastDrawn", SqliteType.Text);
        return command;
    }

    private static SqliteCommand CreateRecordInsertCommand(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "INSERT INTO records (member_key, ordinal, record_id, record_number, record_name, record_gender, " +
            "record_group, draw_gender, draw_group, course_name, history_group, draw_method, draw_numbers, " +
            "draw_time, draw_round_id, weight, row_hash) " +
            "VALUES ($key, $ordinal, $recordId, $recordNumber, $recordName, $recordGender, $recordGroup, " +
            "$drawGender, $drawGroup, $courseName, $historyGroup, $drawMethod, $drawNumbers, $drawTime, " +
            "$drawRoundId, $weight, $rowHash)";
        AddRecordParameters(command);
        return command;
    }

    private static SqliteCommand CreateRecordUpdateCommand(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "UPDATE records SET record_id = $recordId, record_number = $recordNumber, record_name = $recordName, " +
            "record_gender = $recordGender, record_group = $recordGroup, draw_gender = $drawGender, " +
            "draw_group = $drawGroup, course_name = $courseName, history_group = $historyGroup, " +
            "draw_method = $drawMethod, draw_numbers = $drawNumbers, draw_time = $drawTime, " +
            "draw_round_id = $drawRoundId, weight = $weight, row_hash = $rowHash " +
            "WHERE member_key = $key AND ordinal = $ordinal";
        AddRecordParameters(command);
        return command;
    }

    private static void AddRecordParameters(SqliteCommand command)
    {
        command.Parameters.Add("$key", SqliteType.Text);
        command.Parameters.Add("$ordinal", SqliteType.Integer);
        command.Parameters.Add("$recordId", SqliteType.Text);
        command.Parameters.Add("$recordNumber", SqliteType.Text);
        command.Parameters.Add("$recordName", SqliteType.Text);
        command.Parameters.Add("$recordGender", SqliteType.Text);
        command.Parameters.Add("$recordGroup", SqliteType.Text);
        command.Parameters.Add("$drawGender", SqliteType.Text);
        command.Parameters.Add("$drawGroup", SqliteType.Text);
        command.Parameters.Add("$courseName", SqliteType.Text);
        command.Parameters.Add("$historyGroup", SqliteType.Text);
        command.Parameters.Add("$drawMethod", SqliteType.Integer);
        command.Parameters.Add("$drawNumbers", SqliteType.Integer);
        command.Parameters.Add("$drawTime", SqliteType.Text);
        command.Parameters.Add("$drawRoundId", SqliteType.Text);
        command.Parameters.Add("$weight", SqliteType.Real);
        command.Parameters.Add("$rowHash", SqliteType.Integer);
    }

    private static void UpsertMember(SqliteCommand command, string key, History member)
    {
        command.Parameters["$key"].Value = key;
        command.Parameters["$missed"].Value = member.RoundsMissed;
        command.Parameters["$count"].Value = member.TotalCount;
        command.Parameters["$lastDrawn"].Value = FormatTimestamp(member.LastDrawnTime);
        command.ExecuteNonQuery();
    }

    private static void InsertRecords(
        SqliteCommand command,
        string key,
        IReadOnlyList<HistoryItem> items,
        IReadOnlyList<long> hashes,
        int startIndex)
    {
        command.Parameters["$key"].Value = key;
        for (var index = startIndex; index < items.Count; index++)
        {
            SetRecordParameters(command, index, items[index], hashes[index]);
            command.ExecuteNonQuery();
        }
    }

    private static void UpdateRecord(
        SqliteCommand command,
        string key,
        int ordinal,
        HistoryItem item,
        long hash)
    {
        command.Parameters["$key"].Value = key;
        SetRecordParameters(command, ordinal, item, hash);
        command.ExecuteNonQuery();
    }

    private static void SetRecordParameters(SqliteCommand command, int ordinal, HistoryItem item, long hash)
    {
        command.Parameters["$ordinal"].Value = ordinal;
        command.Parameters["$recordId"].Value = item.RecordId;
        command.Parameters["$recordNumber"].Value = item.RecordNumber;
        command.Parameters["$recordName"].Value = item.RecordName;
        command.Parameters["$recordGender"].Value = item.RecordGender;
        command.Parameters["$recordGroup"].Value = item.RecordGroup;
        command.Parameters["$drawGender"].Value = item.DrawGender;
        command.Parameters["$drawGroup"].Value = item.DrawGroup;
        command.Parameters["$courseName"].Value = item.CourseName;
        command.Parameters["$historyGroup"].Value = item.HistoryGroup;
        command.Parameters["$drawMethod"].Value = item.DrawMethod;
        command.Parameters["$drawNumbers"].Value = item.DrawNumbers;
        command.Parameters["$drawTime"].Value = FormatTimestamp(item.DrawTime);
        command.Parameters["$drawRoundId"].Value = item.DrawRoundId;
        command.Parameters["$weight"].Value = item.Weight;
        command.Parameters["$rowHash"].Value = hash;
    }

    private static void DeleteMemberRecords(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM records WHERE member_key = $key";
        command.Parameters.AddWithValue("$key", key);
        command.ExecuteNonQuery();
    }

    private static HistorySnapshot ReadSnapshot(string databasePath, ILogger logger)
    {
        using var connection = Open(databasePath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM meta";
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                meta[reader.GetString(0)] = reader.GetString(1);
        }

        if (!meta.TryGetValue("format", out var format) ||
            !string.Equals(format, FormatMarker, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected history database format: {format ?? "<missing>"}");

        var snapshot = new HistorySnapshot
        {
            TotalRounds = ReadInt(meta, "total_rounds"),
            TotalStats = ReadInt(meta, "total_stats")
        };

        using (var groups = connection.CreateCommand())
        {
            groups.CommandText = "SELECT name, created_at, last_used_at FROM groups";
            using var reader = groups.ExecuteReader();
            while (reader.Read())
                snapshot.Groups.Add(new KeyValuePair<string, HistoryGroupInfo>(
                    reader.GetString(0),
                    new HistoryGroupInfo
                    {
                        CreatedAt = ParseTimestamp(reader.GetString(1)),
                        LastUsedAt = ParseTimestamp(reader.GetString(2))
                    }));
        }

        using (var counters = connection.CreateCommand())
        {
            counters.CommandText = "SELECT kind, name, value FROM counters";
            using var reader = counters.ExecuteReader();
            while (reader.Read())
            {
                var pair = new KeyValuePair<string, int>(reader.GetString(1), (int)reader.GetInt64(2));
                if (string.Equals(reader.GetString(0), "group_stat", StringComparison.Ordinal))
                    snapshot.GroupStats.Add(pair);
                else
                    snapshot.GenderStatus.Add(pair);
            }
        }

        var members = new Dictionary<string, History>(StringComparer.Ordinal);
        using (var memberCommand = connection.CreateCommand())
        {
            memberCommand.CommandText = "SELECT member_key, rounds_missed, total_count, last_drawn_at FROM members";
            using var reader = memberCommand.ExecuteReader();
            while (reader.Read())
            {
                var member = new History
                {
                    RoundsMissed = (int)reader.GetInt64(1),
                    TotalCount = (int)reader.GetInt64(2),
                    LastDrawnTime = ParseTimestamp(reader.GetString(3))
                };
                members[reader.GetString(0)] = member;
                snapshot.MemberOrder.Add(reader.GetString(0));
            }
        }

        using (var recordCommand = connection.CreateCommand())
        {
            recordCommand.CommandText =
                "SELECT member_key, record_id, record_number, record_name, record_gender, record_group, draw_gender, " +
                "draw_group, course_name, history_group, draw_method, draw_numbers, draw_time, draw_round_id, weight " +
                "FROM records ORDER BY member_key, ordinal";
            using var reader = recordCommand.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (!members.TryGetValue(key, out var member))
                {
                    // 记录比成员行更完整时才补成员行（历史文件被外部工具裁剪过的情况）。
                    member = new History();
                    members[key] = member;
                    snapshot.MemberOrder.Add(key);
                }

                member.Histories.Add(new HistoryItem
                {
                    RecordId = reader.GetString(1),
                    RecordNumber = reader.GetString(2),
                    RecordName = reader.GetString(3),
                    RecordGender = reader.GetString(4),
                    RecordGroup = reader.GetString(5),
                    DrawGender = reader.GetString(6),
                    DrawGroup = reader.GetString(7),
                    CourseName = reader.GetString(8),
                    HistoryGroup = reader.GetString(9),
                    DrawMethod = (int)reader.GetInt64(10),
                    DrawNumbers = (int)reader.GetInt64(11),
                    DrawTime = ParseTimestamp(reader.GetString(12)),
                    DrawRoundId = reader.GetString(13),
                    Weight = reader.GetDouble(14)
                });
            }
        }

        foreach (var key in snapshot.MemberOrder)
        {
            if (members.TryGetValue(key, out var member))
                snapshot.Members.Add(new KeyValuePair<string, History>(key, member));
        }

        logger.LogInformation("Loaded history database: {Path} ({Members} members)", databasePath,
            snapshot.Members.Count);
        return snapshot;
    }

    private static HistorySnapshot SnapshotOf(StudentHistory history)
    {
        var snapshot = new HistorySnapshot
        {
            TotalRounds = history.TotalRounds,
            TotalStats = history.TotalStats
        };
        foreach (var pair in history.Students)
            snapshot.Members.Add(new KeyValuePair<string, History>(pair.Key, pair.Value));
        foreach (var pair in history.GroupStats)
            snapshot.GroupStats.Add(pair);
        foreach (var pair in history.GenderStatus)
            snapshot.GenderStatus.Add(pair);
        foreach (var pair in history.HistoryGroups)
            snapshot.Groups.Add(pair);
        return snapshot;
    }

    private static HistorySnapshot SnapshotOf(PrizeHistory history)
    {
        var snapshot = new HistorySnapshot
        {
            TotalRounds = history.TotalRounds,
            TotalStats = history.TotalStats
        };
        foreach (var pair in history.Prizes)
            snapshot.Members.Add(new KeyValuePair<string, History>(pair.Key, pair.Value));
        foreach (var pair in history.GroupStats)
            snapshot.GroupStats.Add(pair);
        foreach (var pair in history.GenderStatus)
            snapshot.GenderStatus.Add(pair);
        foreach (var pair in history.HistoryGroups)
            snapshot.Groups.Add(pair);
        return snapshot;
    }

    private static SqliteConnection Open(string databasePath)
    {
        // Pooling 关掉：档案改名/删除是普通文件操作，任何残留的池化连接都会让它在 Windows 上失败。
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY NOT NULL, value TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS groups (" +
            "name TEXT PRIMARY KEY NOT NULL, created_at TEXT NOT NULL, last_used_at TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS counters (" +
            "kind TEXT NOT NULL, name TEXT NOT NULL, value INTEGER NOT NULL, PRIMARY KEY (kind, name));" +
            "CREATE TABLE IF NOT EXISTS members (" +
            "member_key TEXT PRIMARY KEY NOT NULL, rounds_missed INTEGER NOT NULL, total_count INTEGER NOT NULL, " +
            "last_drawn_at TEXT NOT NULL);" +
            "CREATE TABLE IF NOT EXISTS records (" +
            "member_key TEXT NOT NULL, ordinal INTEGER NOT NULL, record_id TEXT NOT NULL, record_number TEXT NOT NULL, " +
            "record_name TEXT NOT NULL, record_gender TEXT NOT NULL, record_group TEXT NOT NULL, " +
            "draw_gender TEXT NOT NULL, draw_group TEXT NOT NULL, course_name TEXT NOT NULL, " +
            "history_group TEXT NOT NULL, draw_method INTEGER NOT NULL, draw_numbers INTEGER NOT NULL, " +
            "draw_time TEXT NOT NULL, draw_round_id TEXT NOT NULL, weight REAL NOT NULL, " +
            "row_hash INTEGER NOT NULL DEFAULT 0, " +
            "PRIMARY KEY (member_key, ordinal));";
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadInt(IReadOnlyDictionary<string, string> meta, string key) =>
        meta.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : 0;

    private static string FormatTimestamp(DateTime value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static DateTime ParseTimestamp(string value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTime.MinValue;

    /// <summary>
    ///     一行记录的内容指纹（FNV-1a 64）。只用来判断"这行和磁盘上的那行是否相同"，不参与任何身份或校验语义。
    /// </summary>
    private static long ComputeRowHash(HistoryItem item)
    {
        unchecked
        {
            var hash = FnvOffset;
            hash = Mix(hash, item.RecordId);
            hash = Mix(hash, item.RecordNumber);
            hash = Mix(hash, item.RecordName);
            hash = Mix(hash, item.RecordGender);
            hash = Mix(hash, item.RecordGroup);
            hash = Mix(hash, item.DrawGender);
            hash = Mix(hash, item.DrawGroup);
            hash = Mix(hash, item.CourseName);
            hash = Mix(hash, item.HistoryGroup);
            hash = Mix(hash, item.DrawMethod);
            hash = Mix(hash, item.DrawNumbers);
            hash = Mix(hash, item.DrawTime.Ticks);
            hash = Mix(hash, item.DrawRoundId);
            hash = Mix(hash, BitConverter.DoubleToInt64Bits(item.Weight));
            return (long)hash;
        }
    }

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static ulong Mix(ulong hash, string? value)
    {
        unchecked
        {
            if (value is null)
                return (hash ^ 0xFFFF_FFFFUL) * FnvPrime;

            foreach (var character in value)
            {
                hash ^= character;
                hash *= FnvPrime;
            }

            // 分隔符：避免 ("ab","c") 与 ("a","bc") 得到同一个指纹。
            return (hash ^ 0x2FUL) * FnvPrime;
        }
    }

    private static ulong Mix(ulong hash, long value)
    {
        unchecked
        {
            hash ^= (ulong)value;
            return hash * FnvPrime;
        }
    }

    private sealed class HistorySnapshot
    {
        public int TotalRounds { get; set; }
        public int TotalStats { get; set; }

        public List<KeyValuePair<string, History>> Members { get; } = [];
        public List<string> MemberOrder { get; } = [];
        public List<KeyValuePair<string, int>> GroupStats { get; } = [];
        public List<KeyValuePair<string, int>> GenderStatus { get; } = [];
        public List<KeyValuePair<string, HistoryGroupInfo>> Groups { get; } = [];
    }
}
