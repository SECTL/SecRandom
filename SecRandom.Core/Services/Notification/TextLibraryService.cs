using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models.Notification;

namespace SecRandom.Core.Services.Notification;

public sealed record TextLibraryInfo(string Id, string Name, bool IsDefault);

/// <summary>SQLite-backed fullscreen text libraries. The selected library is an editor context only.</summary>
public sealed class TextLibraryService
{
    private const int SchemaVersion = 1;
    private const int ApplicationId = 1397904460;
    private readonly ILogger<TextLibraryService> _logger;
    private readonly object _gate = new();
    private string _selectedLibraryId = "";

    public TextLibraryService(ILogger<TextLibraryService> logger)
        : this(logger, Path.Combine(SecRandom.Shared.Utils.GetDirectoryPath("texts"), "library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension)) { }

    public TextLibraryService(ILogger<TextLibraryService> logger, string filePath)
    {
        _logger = logger;
        FilePath = Path.GetFullPath(filePath);
        Reload();
    }

    public string FilePath { get; }
    public event EventHandler? Changed;
    public string DefaultLibraryId { get; private set; } = "";
    public string? LoadError { get; private set; }
    public string SelectedLibraryId => _selectedLibraryId;
    public IReadOnlyList<TextLibraryInfo> Libraries
    {
        get
        {
            lock (_gate)
            {
                if (LoadError is not null) return [];
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT id, name FROM libraries ORDER BY ordinal, name";
                using var reader = command.ExecuteReader();
                var result = new List<TextLibraryInfo>();
                while (reader.Read())
                    result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(0) == DefaultLibraryId));
                return result;
            }
        }
    }

    public IReadOnlyList<TextLibraryEntry> Entries => GetEntries(_selectedLibraryId);
    public int TotalUseCount => (int)Math.Min(int.MaxValue, Entries.Sum(e => (long)e.UseCount));
    public double AverageUseCount => TextLibraryRotation.AverageUseCount(Entries);
    public int EligibleCount => TextLibraryRotation.GetEligible(Entries, AverageUseCount).Count;

    public void Reload()
    {
        lock (_gate)
        {
            try
            {
                Initialize();
                using var connection = Open();
                if (Convert.ToInt32(ScalarValue(connection, "PRAGMA application_id")) != ApplicationId)
                    throw new InvalidDataException("Unrecognized text library database.");
                DefaultLibraryId = Scalar(connection, "SELECT value FROM meta WHERE key='default_library_id'")
                    ?? throw new InvalidDataException("Text library has no default library.");
                if (!LibraryExists(connection, DefaultLibraryId))
                    throw new InvalidDataException("Text library default is missing.");
                _ = ReadEntries(connection, null, DefaultLibraryId);
                if (string.IsNullOrEmpty(_selectedLibraryId) || !LibraryExists(connection, _selectedLibraryId))
                    _selectedLibraryId = DefaultLibraryId;
                LoadError = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or SqliteException or InvalidDataException or NotSupportedException)
            {
                LoadError = exception.Message;
                DefaultLibraryId = "";
                _selectedLibraryId = "";
                _logger.LogWarning(exception, "Text library could not be loaded: {Path}", FilePath);
            }
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Save() { }

    public static void ValidateArchiveCandidate(string directory)
    {
        var database = Path.Combine(directory, "library" + SecRandom.Shared.Utils.TextLibraryDatabaseExtension);
        if (File.Exists(database))
        {
            using var connection = OpenPath(database, SqliteOpenMode.ReadOnly);
            var defaultId = Scalar(connection, "SELECT value FROM meta WHERE key='default_library_id'");
            if (Convert.ToInt32(ScalarValue(connection, "PRAGMA application_id")) != ApplicationId ||
                Convert.ToInt32(ScalarValue(connection, "PRAGMA user_version")) != SchemaVersion ||
                !string.Equals(Scalar(connection, "PRAGMA integrity_check"), "ok", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(defaultId) || !LibraryExists(connection, defaultId))
                throw new InvalidDataException("Invalid or unsupported text library database.");
            _ = ReadEntries(connection, null, defaultId);
            return;
        }
        var legacy = Path.Combine(directory, "library.json");
        if (!File.Exists(legacy)) return;
        _ = ReadLegacyDocument(legacy);
    }

    public bool SelectLibrary(string id)
    {
        lock (_gate)
        {
            if (LoadError is not null) return false;
            using var connection = Open();
            if (!LibraryExists(connection, id)) return false;
            _selectedLibraryId = id;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public string CreateLibrary(string name, bool copyCurrent = false)
    {
        name = ValidateName(name);
        var id = NewId();
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            EnsureUniqueName(connection, tx, name, null);
            Execute(connection, tx, "INSERT INTO libraries(id,name,ordinal) VALUES($id,$name,(SELECT COALESCE(MAX(ordinal),-1)+1 FROM libraries))", ("$id", id), ("$name", name));
            if (copyCurrent)
            {
                foreach (var entry in ReadEntries(connection, tx, _selectedLibraryId))
                    InsertEntry(connection, tx, id, new TextLibraryEntry { Id = NewId(), Text = entry.Text, Enabled = entry.Enabled });
            }
            tx.Commit();
            _selectedLibraryId = id;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return id;
    }

    public void RenameLibrary(string id, string name)
    {
        name = ValidateName(name);
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            EnsureUniqueName(connection, tx, name, id);
            Execute(connection, tx, "UPDATE libraries SET name=$name WHERE id=$id", ("$name", name), ("$id", id));
            tx.Commit();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool DeleteLibrary(string id)
    {
        if (id == DefaultLibraryId) return false;
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            Execute(connection, tx, "DELETE FROM entries WHERE library_id=$id", ("$id", id));
            var count = Execute(connection, tx, "DELETE FROM libraries WHERE id=$id", ("$id", id));
            tx.Commit();
            if (count == 0) return false;
            if (_selectedLibraryId == id) _selectedLibraryId = DefaultLibraryId;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public IReadOnlyList<TextLibraryEntry> GetEntries(string libraryId)
    {
        lock (_gate)
        {
            if (LoadError is not null) return [];
            using var connection = Open();
            return ReadEntries(connection, null, libraryId);
        }
    }

    public void ReplaceAll(IEnumerable<string> templates) => WriteTemplates(templates, true);
    public void Append(IEnumerable<string> templates) => WriteTemplates(templates, false);
    public void RestoreDefaults() => ReplaceAll(DefaultTextLibrary.Templates);

    private void WriteTemplates(IEnumerable<string> templates, bool replace)
    {
        var entries = templates.Select(t => TextLibraryImporter.NormalizeTemplate(t, out _))
            .Where(IsUsable).Distinct(StringComparer.Ordinal).ToList();
        if (entries.Count == 0) return;
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            var existing = replace ? new HashSet<string>(StringComparer.Ordinal) :
                ReadEntries(connection, tx, _selectedLibraryId).Select(e => e.Text).ToHashSet(StringComparer.Ordinal);
            if (replace)
            {
                Execute(connection, tx, "DELETE FROM entries WHERE library_id=$id", ("$id", _selectedLibraryId));
                Execute(connection, tx, "UPDATE libraries SET last_picked_id=NULL WHERE id=$id", ("$id", _selectedLibraryId));
            }
            foreach (var text in entries.Where(existing.Add))
                InsertEntry(connection, tx, _selectedLibraryId, new TextLibraryEntry { Id = NewId(), Text = text });
            tx.Commit();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetCounts()
    {
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            Execute(connection, tx, "UPDATE entries SET use_count=0,last_used_utc=NULL WHERE library_id=$id", ("$id", _selectedLibraryId));
            Execute(connection, tx, "UPDATE libraries SET last_picked_id=NULL WHERE id=$id", ("$id", _selectedLibraryId));
            tx.Commit();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public string? PickTemplate() => PickTemplate(_selectedLibraryId);

    public string? PickTemplate(string? libraryId)
    {
        string? result;
        lock (_gate)
        {
            if (LoadError is not null) return null;
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            var id = !string.IsNullOrEmpty(libraryId) && LibraryExists(connection, libraryId, tx) ? libraryId : DefaultLibraryId;
            var entries = ReadEntries(connection, tx, id);
            var eligible = TextLibraryRotation.GetEligible(entries, TextLibraryRotation.AverageUseCount(entries));
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = "SELECT last_picked_id FROM libraries WHERE id=$id";
            command.Parameters.AddWithValue("$id", id);
            var last = command.ExecuteScalar() as string;
            var picked = TextLibraryRotation.Pick(eligible, last, RandomNumberGenerator.GetInt32);
            if (picked is null) return null;
            Execute(connection, tx, "UPDATE entries SET use_count=MIN(use_count+1,2147483647),last_used_utc=$now WHERE id=$entry AND library_id=$id", ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$entry", picked.Id), ("$id", id));
            Execute(connection, tx, "UPDATE libraries SET last_picked_id=$entry WHERE id=$id", ("$entry", picked.Id), ("$id", id));
            tx.Commit();
            result = picked.Text;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public void SetEnabled(string id, bool enabled) => MutateEntry("UPDATE entries SET enabled=$value WHERE id=$entry AND library_id=$library", id, enabled ? 1 : 0);
    public TextLibraryEntry? Add(string template)
    {
        var text = TextLibraryImporter.NormalizeTemplate(template, out _);
        if (!IsUsable(text) || Entries.Any(e => e.Text == text)) return null;
        var entry = new TextLibraryEntry { Id = NewId(), Text = text };
        lock (_gate)
        {
            using var connection = OpenWritable();
            using var tx = connection.BeginTransaction();
            InsertEntry(connection, tx, _selectedLibraryId, entry);
            tx.Commit();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public bool Update(string id, string template)
    {
        var text = TextLibraryImporter.NormalizeTemplate(template, out _);
        if (!IsUsable(text) || Entries.Any(e => e.Id != id && e.Text == text)) return false;
        return MutateEntry("UPDATE entries SET text=$value WHERE id=$entry AND library_id=$library AND text<>$value", id, text);
    }

    public bool Remove(string id) => MutateEntry("DELETE FROM entries WHERE id=$entry AND library_id=$library", id, "");

    private bool MutateEntry(string sql, string id, object value)
    {
        int count;
        lock (_gate)
        {
            using var connection = OpenWritable();
            count = Execute(connection, null, sql, ("$value", value), ("$entry", id), ("$library", _selectedLibraryId));
        }
        if (count > 0) Changed?.Invoke(this, EventArgs.Empty);
        return count > 0;
    }

    private void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        if (File.Exists(FilePath)) return;
        var legacy = Path.Combine(Path.GetDirectoryName(FilePath)!, "library.json");
        TextLibraryDocument? document = null;
        if (File.Exists(legacy))
        {
            document = ReadLegacyDocument(legacy);
        }
        var temporary = FilePath + "." + NewId() + ".tmp";
        try
        {
            using (var connection = OpenPath(temporary))
            using (var tx = connection.BeginTransaction())
            {
                Execute(connection, tx, "CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE libraries(id TEXT PRIMARY KEY,name TEXT NOT NULL,ordinal INTEGER NOT NULL,last_picked_id TEXT); CREATE TABLE entries(id TEXT PRIMARY KEY,library_id TEXT NOT NULL,text TEXT NOT NULL,enabled INTEGER NOT NULL,ordinal INTEGER NOT NULL,use_count INTEGER NOT NULL,last_used_utc TEXT); PRAGMA application_id=1397904460; PRAGMA user_version=1;");
                var id = NewId();
                Execute(connection, tx, "INSERT INTO meta VALUES('default_library_id',$id); INSERT INTO libraries(id,name,ordinal,last_picked_id) VALUES($id,'Default',0,$last)", ("$id", id), ("$last", (object?)document?.LastPickedId ?? DBNull.Value));
                var entries = document?.Texts ?? DefaultTextLibrary.Templates.Select(t => new TextLibraryEntry { Id = NewId(), Text = t }).ToList();
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in entries.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Text)))
                {
                    if (string.IsNullOrEmpty(entry.Id) || !ids.Add(entry.Id)) { entry.Id = NewId(); ids.Add(entry.Id); }
                    entry.UseCount = Math.Max(0, entry.UseCount);
                    InsertEntry(connection, tx, id, entry);
                }
                tx.Commit();
            }
            File.Move(temporary, FilePath);
            if (document is not null)
            {
                try { File.Move(legacy, legacy + ".migrated", overwrite: false); }
                catch (IOException ex) { _logger.LogWarning(ex, "Could not move legacy text library after migration"); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private SqliteConnection Open() => OpenPath(FilePath, SqliteOpenMode.ReadWrite);
    private static TextLibraryDocument ReadLegacyDocument(string path)
    {
        var json = File.ReadAllText(path);
        using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object ||
            !parsed.RootElement.TryGetProperty("texts", out var texts) || texts.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid legacy text library entries.");
        var options = new JsonSerializerOptions(ConfigServiceBase.JsonOptions) { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var document = JsonSerializer.Deserialize<TextLibraryDocument>(json, options);
        if (document is null || document.Texts is null || document.FormatVersion > 1)
            throw new InvalidDataException("Invalid or unsupported legacy text library.");
        return document;
    }
    private SqliteConnection OpenWritable()
    {
        if (LoadError is not null) throw new InvalidDataException("Text library is unavailable: " + LoadError);
        var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        if (Convert.ToInt32(command.ExecuteScalar()) > SchemaVersion)
        {
            connection.Dispose();
            throw new InvalidDataException("Text library database is newer than supported.");
        }
        return connection;
    }
    private static SqliteConnection OpenPath(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    private static string? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar() as string;
    }
    private static object? ScalarValue(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar();
    }
    private static bool LibraryExists(SqliteConnection connection, string id, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = "SELECT 1 FROM libraries WHERE id=$id"; command.Parameters.AddWithValue("$id", id); return command.ExecuteScalar() is not null;
    }
    private static void EnsureUniqueName(SqliteConnection connection, SqliteTransaction tx, string name, string? exceptId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT name FROM libraries WHERE id<>$id";
        command.Parameters.AddWithValue("$id", exceptId ?? "");
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(0), name, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Library name already exists.");
    }
    private static int Execute(SqliteConnection connection, SqliteTransaction? tx, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand(); command.Transaction = tx; command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command.ExecuteNonQuery();
    }
    private static void InsertEntry(SqliteConnection connection, SqliteTransaction tx, string library, TextLibraryEntry entry) =>
        Execute(connection, tx, "INSERT INTO entries(id,library_id,text,enabled,ordinal,use_count,last_used_utc) VALUES($id,$library,$text,$enabled,(SELECT COALESCE(MAX(ordinal),-1)+1 FROM entries WHERE library_id=$library),$count,$last)", ("$id", entry.Id), ("$library", library), ("$text", entry.Text), ("$enabled", entry.Enabled ? 1 : 0), ("$count", entry.UseCount), ("$last", (object?)entry.LastUsedUtc?.ToString("O") ?? DBNull.Value));
    private static List<TextLibraryEntry> ReadEntries(SqliteConnection connection, SqliteTransaction? tx, string library)
    {
        using var command = connection.CreateCommand(); command.Transaction = tx;
        command.CommandText = "SELECT id,text,enabled,use_count,last_used_utc FROM entries WHERE library_id=$id ORDER BY ordinal";
        command.Parameters.AddWithValue("$id", library);
        using var reader = command.ExecuteReader();
        var entries = new List<TextLibraryEntry>();
        while (reader.Read()) entries.Add(new TextLibraryEntry { Id = reader.GetString(0), Text = reader.GetString(1), Enabled = reader.GetInt64(2) != 0, UseCount = reader.GetInt32(3), LastUsedUtc = reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4), System.Globalization.CultureInfo.InvariantCulture) });
        return entries;
    }
    private static string ValidateName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100 ? name.Trim() : throw new ArgumentException("Invalid library name.");
    private static bool IsUsable(string text) => text.Length is > 0 and <= TextLibraryImporter.MaxTextLength && FullscreenTextTemplate.HasSinglePlaceholder(text);
    private static string NewId() => Guid.NewGuid().ToString("N");
}
