using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Core.Services.Archive;
using SecRandom.Services.Security;
using SecRandom.Shared;

namespace SecRandom.Services.ImportExport;

/// <summary>
///     One cached cloud backup key: the salt it was derived with and the derived AES-256-GCM key.
///     The passphrase itself is deliberately never written to disk, so what this file exposes is exactly
///     the backups that key covers — and only to whoever can read it.
/// </summary>
public sealed record CloudBackupKeyEntry(
    string Salt,
    byte[] Key,
    CloudBackupKdfParameters Parameters,
    DateTimeOffset CreatedUtc);

/// <summary>
///     Caches the derived cloud backup keys in
///     <c>data/config/security/cloud-backup-keys.json</c>, beside the security credentials and for the
///     same reason: that directory is excluded from every export, cloud backup, and Android provider
///     view, and it is restricted to the current user. Holding the derived key there is what lets
///     automatic cloud backup run unattended and lets this device restore its own backups without
///     asking for the passphrase again; a machine that has never seen the passphrase still has to type
///     it once per backup salt.
///     The protection of this file is the directory permission and nothing else: a reader of the file
///     gets a ready-to-use key without the passphrase, which is why it is excluded from every archive
///     and never travels to another device.
///     The newest entry is the key new uploads use. Older entries are retained so changing the
///     passphrase does not strand the backups that already exist.
/// </summary>
public sealed class CloudBackupKeyStore
{
    private const int FormatVersion = 1;

    /// <summary>
    ///     Bounded key history. Every passphrase change adds one entry, and each entry only covers the
    ///     salts used from that point on, so an older backup beyond this window asks for its own
    ///     passphrase again instead of the file growing without limit.
    /// </summary>
    private const int MaxEntries = 8;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private bool _directoryProtected;

    public bool HasKey
    {
        get
        {
            lock (_gate)
                return LoadCore().Keys.Count > 0;
        }
    }

    /// <summary>The key new uploads are encrypted with.</summary>
    public CloudBackupKeyEntry? GetActiveEntry()
    {
        lock (_gate)
            return LoadCore().Keys.FirstOrDefault() is { } entry ? ToEntry(entry) : null;
    }

    /// <summary>The cached key for one manifest salt, or null when this device never derived it.</summary>
    public CloudBackupKeyEntry? FindBySalt(string salt)
    {
        if (string.IsNullOrWhiteSpace(salt))
            return null;

        lock (_gate)
        {
            return LoadCore().Keys
                .Where(entry => string.Equals(entry.Salt, salt, StringComparison.Ordinal))
                .Select(ToEntry)
                .FirstOrDefault();
        }
    }

    /// <summary>
    ///     Derives a key from a passphrase and caches it. <paramref name="salt" /> is the account's
    ///     existing salt when there already is an encrypted backup, so a second machine derives the
    ///     same key instead of a private one; a new passphrase gets a fresh random salt.
    /// </summary>
    public CloudBackupKeyEntry CreateKey(string passphrase, string? salt = null,
        CloudBackupKdfParameters? parameters = null)
    {
        var effectiveParameters = parameters ?? CloudBackupKdfParameters.Default;
        var effectiveSalt = string.IsNullOrWhiteSpace(salt) ? CloudBackupCipher.CreateSalt() : salt;
        var key = CloudBackupCipher.DeriveKey(passphrase, effectiveSalt, effectiveParameters);
        try
        {
            return SaveDerivedKey(effectiveSalt, key, effectiveParameters);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    ///     Caches an already derived key, e.g. after a restore proved the passphrase against a
    ///     manifest, so the next restore of the same backup does not ask again.
    /// </summary>
    public CloudBackupKeyEntry SaveDerivedKey(string salt, byte[] key, CloudBackupKdfParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(parameters);
        if (key.Length != CloudBackupEncryption.KeyBytes)
            throw new ArgumentException("Cloud backup key must be 32 bytes.", nameof(key));

        lock (_gate)
        {
            var store = LoadCore();
            var entry = new StoredCloudBackupKey
            {
                Salt = salt,
                Key = Convert.ToBase64String(key),
                MemoryKiB = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
                Parallelism = parameters.Parallelism,
                CreatedUtc = DateTimeOffset.UtcNow
            };

            store.Keys.RemoveAll(existing => string.Equals(existing.Salt, salt, StringComparison.Ordinal));
            store.Keys.Insert(0, entry);
            if (store.Keys.Count > MaxEntries)
                store.Keys.RemoveRange(MaxEntries, store.Keys.Count - MaxEntries);
            SaveCore(store);
            return ToEntry(entry);
        }
    }

    /// <summary>Forgets every cached key. Existing cloud backups then need their passphrase again.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            var path = GetPath();
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
                // The key file is a convenience cache: a failed delete must not break the backup page.
            }
        }
    }

    private static CloudBackupKeyEntry ToEntry(StoredCloudBackupKey entry) => new(
        entry.Salt,
        Convert.FromBase64String(entry.Key),
        new CloudBackupKdfParameters(entry.MemoryKiB, entry.Iterations, entry.Parallelism),
        entry.CreatedUtc);

    private static string GetPath() => Utils.GetFilePath("config", "security", "cloud-backup-keys.json");

    private static CloudBackupKeyFile LoadCore()
    {
        try
        {
            var path = GetPath();
            if (!File.Exists(path))
                return new CloudBackupKeyFile();

            var parsed = JsonSerializer.Deserialize<CloudBackupKeyFile>(File.ReadAllText(path));
            if (parsed is null || parsed.FormatVersion != FormatVersion || parsed.Keys is null)
                return new CloudBackupKeyFile();

            // A truncated or hand-edited file must not turn into an unhandled crash on the backup page.
            parsed.Keys.RemoveAll(entry => !IsUsable(entry));
            return parsed;
        }
        catch (Exception)
        {
            return new CloudBackupKeyFile();
        }
    }

    private static bool IsUsable(StoredCloudBackupKey? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Salt) || string.IsNullOrWhiteSpace(entry.Key))
            return false;

        try
        {
            return Convert.FromBase64String(entry.Key).Length == CloudBackupEncryption.KeyBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private void SaveCore(CloudBackupKeyFile store)
    {
        var path = GetPath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            if (!_directoryProtected)
            {
                _directoryProtected = true;
                SecurityPathProtection.RestrictDirectoryToOwner(directory);
            }
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, SerializerOptions));
            SecurityPathProtection.RestrictFileToOwner(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private sealed class CloudBackupKeyFile
    {
        [JsonPropertyName("format_version")] public int FormatVersion { get; init; } = CloudBackupKeyStore.FormatVersion;
        [JsonPropertyName("keys")] public List<StoredCloudBackupKey> Keys { get; init; } = [];
    }

    private sealed class StoredCloudBackupKey
    {
        [JsonPropertyName("salt")] public string Salt { get; init; } = string.Empty;
        [JsonPropertyName("key")] public string Key { get; init; } = string.Empty;
        [JsonPropertyName("memory_kib")] public int MemoryKiB { get; init; }
        [JsonPropertyName("iterations")] public int Iterations { get; init; }
        [JsonPropertyName("parallelism")] public int Parallelism { get; init; }
        [JsonPropertyName("created_utc")] public DateTimeOffset CreatedUtc { get; init; }
    }
}
