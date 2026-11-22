using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Core.Services.DataRoot;

/// <summary>
///     Rewrites the data files that may carry an absolute path from the old data root. Only two shapes exist today:
///     attached draw-image settings under <c>list/</c> (<c>ImagePath</c>) and the proof attestation queue
///     (<c>path</c>). Values that live outside the old root — a user-picked external image, a URL — are left
///     byte-identical, because the migration must never claim to know what they meant.
/// </summary>
public static class DataRootPathRewriter
{
    private static readonly JsonSerializerOptions CompactWriteOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions IndentedWriteOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonNodeOptions NodeOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    /// <summary>Rewrites every rewritable file inside the staging copy and returns how many values changed.</summary>
    public static int RewriteStaging(string stagingRoot, string oldRoot, string newRoot)
    {
        var changed = 0;

        var listDirectory = Path.Combine(stagingRoot, "list");
        if (Directory.Exists(listDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(listDirectory, "*.json", SearchOption.AllDirectories))
                changed += RewriteFile(file, oldRoot, newRoot, imageOnly: true);
        }

        var attestationQueue = Path.Combine(stagingRoot, "proofs", "attestation-queue.json");
        if (File.Exists(attestationQueue))
            changed += RewriteFile(attestationQueue, oldRoot, newRoot, imageOnly: false);

        changed += RewriteRosterDatabases(stagingRoot, oldRoot, newRoot);
        return changed;
    }

    /// <summary>
    /// Rewrites image paths in roster SQLite databases copied into a migration staging tree.
    /// Unknown, malformed, and non-roster databases are ignored without opening them for writes.
    /// </summary>
    public static int RewriteRosterDatabases(string stagingRoot, string oldRoot, string newRoot)
    {
        var changed = 0;
        var directory = Path.Combine(stagingRoot, "list");
        if (!Directory.Exists(directory))
            return changed;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*.srl", SearchOption.AllDirectories).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return changed;
        }

        foreach (var file in files)
            changed += RewriteRosterDatabase(file, oldRoot, newRoot);

        return changed;
    }

    /// <summary>Rewrites a recognized roster database transactionally.</summary>
    public static int RewriteRosterDatabase(string databasePath, string oldRoot, string newRoot)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ConnectionString);
            connection.Open();

            if (!IsRosterDatabase(connection))
                return 0;

            using var transaction = connection.BeginTransaction();
            var updates = new List<(string Table, string Payload, long RowId)>();
            var changed = 0;
            changed += ReadPayloads(connection, transaction, "members", oldRoot, newRoot, updates);
            changed += ReadPayloads(connection, transaction, "attached_objects", oldRoot, newRoot, updates);
            if (changed == 0)
            {
                transaction.Rollback();
                return 0;
            }

            foreach (var update in updates)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = $"UPDATE {update.Table} SET payload = $payload WHERE rowid = $rowid";
                command.Parameters.AddWithValue("$payload", update.Payload);
                command.Parameters.AddWithValue("$rowid", update.RowId);
                if (command.ExecuteNonQuery() != 1)
                    throw new InvalidDataException("Roster database payload row disappeared during rewrite.");
            }

            transaction.Commit();
            return changed;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException or
                                           InvalidDataException or NotSupportedException)
        {
            return 0;
        }
    }

    private static bool IsRosterDatabase(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM meta WHERE key = 'format'";
        var format = command.ExecuteScalar() as string;
        var version = format switch
        {
            "SecRandomRosterDatabase/v1" => "1",
            "SecRandomRosterDatabase/v2" => "2",
            _ => null
        };
        if (version is null)
            return false;

        command.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
        if (!string.Equals(command.ExecuteScalar() as string, version, StringComparison.Ordinal))
            return false;

        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('members', 'attached_objects')";
        using var reader = command.ExecuteReader();
        var tables = 0;
        while (reader.Read())
            tables++;
        if (tables != 2)
            return false;

        return HasColumn(connection, "members", "payload") &&
               HasColumn(connection, "attached_objects", "payload");
    }

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info([{table}])";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static int ReadPayloads(SqliteConnection connection, SqliteTransaction transaction, string table,
        string oldRoot, string newRoot, List<(string Table, string Payload, long RowId)> updates)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT rowid, payload FROM {table}";
        using var reader = command.ExecuteReader();
        var changed = 0;
        while (reader.Read())
        {
            var rowId = reader.GetInt64(0);
            var original = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(original))
                continue;

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(original, NodeOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (root is null)
                continue;
            var valueChanges = RewriteNode(root, oldRoot, newRoot, imageOnly: true);
            if (valueChanges == 0)
                continue;

            var options = original.Contains('\n') ? IndentedWriteOptions : CompactWriteOptions;
            updates.Add((table, root.ToJsonString(options), rowId));
            changed += valueChanges;
        }

        reader.Close();
        return changed;
    }

    private static int RewriteFile(string file, string oldRoot, string newRoot, bool imageOnly)
    {
        string original;
        try
        {
            original = File.ReadAllText(file);
        }
        catch (IOException)
        {
            return 0;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(original, NodeOptions);
        }
        catch (JsonException)
        {
            return 0;
        }

        if (root is null)
            return 0;

        var changed = RewriteNode(root, oldRoot, newRoot, imageOnly);
        if (changed == 0)
            return 0;

        // Keep the file's existing formatting: a compact settings file must stay compact, and an indented one
        // must not be flattened, otherwise the migration would produce a spurious whole-file diff.
        var writeOptions = original.Contains('\n') ? IndentedWriteOptions : CompactWriteOptions;
        File.WriteAllText(file, root.ToJsonString(writeOptions));
        return changed;
    }

    private static int RewriteNode(JsonNode? node, string oldRoot, string newRoot, bool imageOnly)
    {
        var changed = 0;

        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (Matches(property.Key, imageOnly) &&
                        TryReadString(property.Value, out var text) &&
                        RewriteValue(text, oldRoot, newRoot) is var rewritten &&
                        !string.Equals(rewritten, text, StringComparison.Ordinal))
                    {
                        jsonObject[property.Key] = rewritten;
                        changed++;
                        continue;
                    }

                    changed += RewriteNode(property.Value, oldRoot, newRoot, imageOnly);
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var item = jsonArray[index];
                    if (TryReadString(item, out var text) &&
                        RewriteValue(text, oldRoot, newRoot) is var rewritten &&
                        !string.Equals(rewritten, text, StringComparison.Ordinal))
                    {
                        jsonArray[index] = rewritten;
                        changed++;
                        continue;
                    }

                    changed += RewriteNode(item, oldRoot, newRoot, imageOnly);
                }

                break;
        }

        return changed;
    }

    private static bool Matches(string propertyName, bool imageOnly)
    {
        if (string.Equals(propertyName, "ImagePath", StringComparison.OrdinalIgnoreCase))
            return true;

        if (imageOnly)
            return false;

        return string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(propertyName, "proofPath", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadString(JsonNode? node, out string text)
    {
        text = string.Empty;
        if (node is not JsonValue value || !value.TryGetValue<string>(out var parsed) || parsed is null)
            return false;

        text = parsed;
        return true;
    }

    private static string RewriteValue(string value, string oldRoot, string newRoot)
    {
        if (string.IsNullOrWhiteSpace(value) || DataPathReference.IsStoredRelative(value))
            return value;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(value.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or
                                           PathTooLongException)
        {
            return value;
        }

        if (!DataRootPaths.IsSameOrNested(oldRoot, fullPath))
            return value;

        var relative = Path.GetRelativePath(oldRoot, fullPath);
        return relative.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
