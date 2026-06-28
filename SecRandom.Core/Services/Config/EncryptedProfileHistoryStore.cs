using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Config;

public sealed class EncryptedProfileHistoryStore
{
    private const string TableName = "profile_history";
    private static readonly string Purpose = "SecRandom.ProfileHistory.v1";
    private readonly ILogger<EncryptedProfileHistoryStore> _logger;
    private readonly object _sync = new();

    public EncryptedProfileHistoryStore(ILogger<EncryptedProfileHistoryStore> logger)
    {
        _logger = logger;
    }

    public bool Exists(ProfileConfigBase config)
    {
        lock (_sync)
        {
            if (!File.Exists(config.ConfigFilePath))
                return false;

            using var connection = OpenConnection(config.ConfigFilePath);
            EnsureSchema(connection);
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT 1 FROM {TableName} WHERE type = $type AND name = $name LIMIT 1;";
            command.Parameters.AddWithValue("$type", GetHistoryType(config));
            command.Parameters.AddWithValue("$name", config.Name);
            return command.ExecuteScalar() is not null;
        }
    }

    public ProfileConfigBase Load(ProfileConfigBase fallback)
    {
        lock (_sync)
        {
            if (TryLoadStored(fallback, out var stored))
                return stored;

            if (!File.Exists(fallback.ConfigFilePath))
                return fallback;

            try
            {
                var json = File.ReadAllText(fallback.ConfigFilePath);
                if (ProfileEncryptionCodec.TryReadEnvelope(json, out _))
                {
                    var decrypted = ProfileEncryptionCodec.DecodeEnvelopeJson(json, Purpose);
                    var loaded = LoadPlain(decrypted, fallback);
                    loaded.Name = fallback.Name;
                    SaveInternal(loaded);
                    return loaded;
                }

                if (!IsLegacyHistoryCandidate(json, fallback))
                    return fallback;

                var legacy = LoadPlain(json, fallback);
                legacy.Name = fallback.Name;
                SaveInternal(legacy);
                return legacy;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法迁移历史记录文件，继续使用内存数据：{Path}", fallback.ConfigFilePath);
                return fallback;
            }
        }
    }

    public void Save(ProfileConfigBase config)
    {
        lock (_sync)
        {
            SaveInternal(config);
        }
    }

    public void Delete(ProfileConfigBase config)
    {
        lock (_sync)
        {
            if (!File.Exists(config.ConfigFilePath))
                return;

            using var connection = OpenConnection(config.ConfigFilePath);
            EnsureSchema(connection);
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {TableName} WHERE type = $type AND name = $name;";
            command.Parameters.AddWithValue("$type", GetHistoryType(config));
            command.Parameters.AddWithValue("$name", config.Name);
            command.ExecuteNonQuery();

            if (IsDatabaseEmpty(connection))
                File.Delete(config.ConfigFilePath);
        }
    }

    public void Rename(ProfileConfigBase config, string newName)
    {
        lock (_sync)
        {
            var oldPath = config.ConfigFilePath;
            if (!File.Exists(oldPath))
                return;

            var newPath = ReplaceNameInPath(oldPath, newName);
            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(newPath))
                File.Delete(newPath);

            using (var connection = OpenConnection(oldPath))
            {
                EnsureSchema(connection);
                using var command = connection.CreateCommand();
                command.CommandText = $"UPDATE {TableName} SET name = $newName WHERE type = $type AND name = $oldName;";
                command.Parameters.AddWithValue("$type", GetHistoryType(config));
                command.Parameters.AddWithValue("$oldName", config.Name);
                command.Parameters.AddWithValue("$newName", newName);
                command.ExecuteNonQuery();
            }

            File.Move(oldPath, newPath);
        }
    }

    private void SaveInternal(ProfileConfigBase config)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(config, ConfigServiceBase.JsonOptions);
        var encrypted = ProfileEncryptionCodec.Encrypt(payload, Purpose);

        using var connection = OpenConnection(config.ConfigFilePath);
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {TableName} (type, name, payload, updated_at) VALUES ($type, $name, $payload, $updatedAt) " +
            $"ON CONFLICT(type, name) DO UPDATE SET payload = excluded.payload, updated_at = excluded.updated_at;";
        command.Parameters.AddWithValue("$type", GetHistoryType(config));
        command.Parameters.AddWithValue("$name", config.Name);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = encrypted;
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private bool TryLoadStored(ProfileConfigBase fallback, out ProfileConfigBase value)
    {
        value = fallback;
        if (!File.Exists(fallback.ConfigFilePath))
            return false;

        using var connection = OpenConnection(fallback.ConfigFilePath);
        EnsureSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {TableName} WHERE type = $type AND name = $name LIMIT 1;";
        command.Parameters.AddWithValue("$type", GetHistoryType(fallback));
        command.Parameters.AddWithValue("$name", fallback.Name);

        var payload = command.ExecuteScalar() as byte[];
        if (payload is null || payload.Length == 0)
            return false;

        try
        {
            var json = ProfileEncryptionCodec.Decrypt(payload, Purpose);
            value = LoadPlain(json, fallback);
            value.Name = fallback.Name;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "解密历史记录失败：{Name}", fallback.Name);
            return false;
        }
    }

    private static ProfileConfigBase LoadPlain(byte[] json, ProfileConfigBase fallback)
    {
        return JsonSerializer.Deserialize(json, fallback.GetType(), ConfigServiceBase.JsonOptions) as ProfileConfigBase ?? fallback;
    }

    private static ProfileConfigBase LoadPlain(string json, ProfileConfigBase fallback)
    {
        return JsonSerializer.Deserialize(json, fallback.GetType(), ConfigServiceBase.JsonOptions) as ProfileConfigBase ?? fallback;
    }

    private static bool IsLegacyHistoryCandidate(string json, ProfileConfigBase fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            return fallback switch
            {
                StudentHistory => root.TryGetProperty("students", out _)
                                   || root.TryGetProperty("group_stats", out _)
                                   || root.TryGetProperty("gender_status", out _),
                PrizeHistory => root.TryGetProperty("prizes", out _)
                                 || root.TryGetProperty("group_stats", out _)
                                 || root.TryGetProperty("gender_status", out _),
                _ => root.EnumerateObject().Any()
            };
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS {TableName} (
                 type TEXT NOT NULL,
                 name TEXT NOT NULL,
                 payload BLOB NOT NULL,
                 updated_at TEXT NOT NULL,
                 PRIMARY KEY (type, name)
             );
             """;
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenConnection(string dbPath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool IsDatabaseEmpty(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT 1 FROM {TableName} LIMIT 1;";
        return command.ExecuteScalar() is null;
    }

    private static string GetHistoryType(ProfileConfigBase config)
    {
        return config switch
        {
            StudentHistory => "student",
            PrizeHistory => "prize",
            _ => config.GetType().FullName ?? config.GetType().Name
        };
    }

    private static string ReplaceNameInPath(string path, string newName)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        return Path.Combine(directory, $"{newName}.sqlite");
    }
}
