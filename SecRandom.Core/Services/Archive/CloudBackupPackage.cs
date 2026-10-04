using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SecRandom.Core.Abstraction;

namespace SecRandom.Core.Services.Archive;

/// <summary>
///     Cloud backup split package: the account cloud service keeps one backup as several small
///     parts plus a trailing manifest. Splitting keeps every upload request well below the
///     cloud-function request-body limit while the manifest carries the per-part and whole-archive
///     SHA-256 values needed to rebuild one verified SecRandom v3 archive.
/// </summary>
public static partial class CloudBackupPackage
{
    public const string Format = "secrandom-cloud-backup";

    /// <summary>Plaintext package: the parts carry the v3 ZIP archive itself.</summary>
    public const int SchemaVersion = 1;

    /// <summary>
    ///     Encrypted package: the parts carry an AES-256-GCM ciphertext of the v3 ZIP archive and the
    ///     manifest describes the key derivation that produced it.
    /// </summary>
    public const int EncryptedSchemaVersion = 2;

    public const string FilePrefix = "SecRandom-cloud-";
    public const string PartExtension = ".srpart";
    public const string ManifestSuffix = "-manifest.json";

    /// <summary>
    ///     Marker an encrypted package carries in every file name. It lets the cloud listing show whether
    ///     a backup is encrypted without downloading its manifest, which would otherwise cost one request
    ///     per backup on every refresh. The manifest stays authoritative: a restore follows its
    ///     <c>encryption</c> block, never this marker.
    /// </summary>
    public const string EncryptionMarker = "-enc";

    /// <summary>
    ///     Longest device alias a backup id carries. The alias is part of every cloud file name, so it
    ///     stays short enough for the whole id to remain inside <see cref="IsSafeBackupId" />'s
    ///     64-character limit and readable in the cloud dashboard listing.
    /// </summary>
    public const int MaxDeviceTagLength = 16;

    /// <summary>
    ///     512 KiB of archive bytes per part. The upload body is a base64 JSON payload, so one part
    ///     stays around 700 KiB and remains safely inside the conservative 1 MiB request budget of
    ///     the SECTL cloud function origin.
    /// </summary>
    public const int DefaultPartBytes = 512 * 1024;

    /// <summary>Conservative request-body budget; the cloud function origin limits bodies near 1 MiB.</summary>
    public const long MaxUploadBodyBytes = 1L * 1024 * 1024;

    private const int MaxPartCount = 64;
    private const long JsonEnvelopeOverheadBytes = 1024;
    private const long MaxArchiveBytes = 16L * 1024 * 1024;

    private static JsonSerializerOptions ManifestJsonOptions => ConfigServiceBase.JsonOptions;

    [GeneratedRegex("^(?<id>.+)-p(?<index>\\d{2,3})of(?<count>\\d{2,3})$", RegexOptions.CultureInvariant)]
    private static partial Regex PartNamePattern();

    /// <summary>
    ///     Shape of an id this package generates: a timestamp, the random suffix, and the optional
    ///     device alias. Only ids matching it report a device alias, so a foreign id uploaded by an
    ///     older build or crafted through the cloud dashboard is never attributed to a device.
    /// </summary>
    [GeneratedRegex("^\\d{8}-\\d{6}-[0-9a-f]{8}(?<tag>_[A-Za-z0-9-]{1,16})?$", RegexOptions.CultureInvariant)]
    private static partial Regex BackupIdPattern();

    public static string CreateBackupId(DateTime utcNow, string? deviceTag = null)
    {
        var id = $"{utcNow:yyyyMMdd-HHmmss}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()}";
        var tag = NormalizeDeviceTag(deviceTag);
        return tag.Length == 0 ? id : $"{id}_{tag}";
    }

    /// <summary>
    ///     Builds the alias stamped on this device's backups. A configured alias wins; otherwise the
    ///     host name is used, and a host name without ASCII-safe characters falls back to a short
    ///     digest so its backups stay attributable. An empty result means the id carries no alias.
    /// </summary>
    public static string BuildDeviceTag(string? deviceName)
    {
        var normalized = NormalizeDeviceTag(deviceName);
        if (normalized.Length > 0)
            return normalized;

        return string.IsNullOrWhiteSpace(deviceName)
            ? string.Empty
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceName.Trim())))[..4].ToLowerInvariant();
    }

    /// <summary>Reads the alias this package encoded into a backup id.</summary>
    public static bool TryGetDeviceTag(string? backupId, out string deviceTag)
    {
        deviceTag = string.Empty;
        if (string.IsNullOrWhiteSpace(backupId))
            return false;

        var match = BackupIdPattern().Match(backupId);
        if (!match.Success || !match.Groups["tag"].Success)
            return false;

        deviceTag = match.Groups["tag"].Value[1..];
        return true;
    }

    private static string NormalizeDeviceTag(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(MaxDeviceTagLength);
        foreach (var character in value.Trim())
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                continue;
            builder.Append(character);
            if (builder.Length == MaxDeviceTagLength)
                break;
        }

        return builder.ToString();
    }

    public static string BuildPartName(string backupId, int index, int partCount, bool encrypted = false) =>
        $"{FilePrefix}{backupId}-p{index:D2}of{partCount:D2}{(encrypted ? EncryptionMarker : string.Empty)}{PartExtension}";

    public static string BuildManifestName(string backupId, bool encrypted = false) =>
        $"{FilePrefix}{backupId}{(encrypted ? EncryptionMarker : string.Empty)}{ManifestSuffix}";

    public static string BuildArchiveName(string backupId) => $"SecRandom_cloud_{backupId}.zip";

    public static bool IsCloudBackupFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.StartsWith(FilePrefix, StringComparison.Ordinal);

    /// <summary>
    ///     Parses a cloud file name produced by this package format. Foreign file names return false
    ///     so personal-cloud listings can keep unrelated platform files out of the backup list.
    ///     <paramref name="isEncrypted" /> reports the name's encryption marker, which is a listing hint
    ///     only; <see cref="CloudBackupManifest.Encryption" /> decides how a payload is really opened.
    /// </summary>
    public static bool TryParseFileName(string? fileName, out string backupId, out int partIndex, out int partCount,
        out bool isManifest, out bool isEncrypted)
    {
        backupId = string.Empty;
        partIndex = 0;
        partCount = 0;
        isManifest = false;
        isEncrypted = false;
        if (!IsCloudBackupFileName(fileName))
            return false;

        var remainder = StripEncryptionMarker(fileName![FilePrefix.Length..], out isEncrypted);
        if (remainder.EndsWith(ManifestSuffix, StringComparison.Ordinal))
        {
            backupId = remainder[..^ManifestSuffix.Length];
            isManifest = true;
            return IsSafeBackupId(backupId);
        }

        if (!remainder.EndsWith(PartExtension, StringComparison.Ordinal))
            return false;

        var match = PartNamePattern().Match(remainder[..^PartExtension.Length]);
        if (!match.Success)
            return false;

        backupId = match.Groups["id"].Value;
        return IsSafeBackupId(backupId)
               && int.TryParse(match.Groups["index"].Value, out partIndex)
               && int.TryParse(match.Groups["count"].Value, out partCount)
               && partIndex >= 1 && partCount >= 1 && partIndex <= partCount;
    }

    /// <summary>Parses a cloud file name without reporting its encryption marker.</summary>
    public static bool TryParseFileName(string? fileName, out string backupId, out int partIndex, out int partCount,
        out bool isManifest) =>
        TryParseFileName(fileName, out backupId, out partIndex, out partCount, out isManifest, out _);

    /// <summary>
    ///     Splits the encryption marker off a file name remainder. The marker sits directly before the
    ///     part/manifest suffix, so a name written before encryption existed parses unchanged.
    /// </summary>
    private static string StripEncryptionMarker(string remainder, out bool isEncrypted)
    {
        isEncrypted = false;
        var suffixLength = remainder.EndsWith(ManifestSuffix, StringComparison.Ordinal)
            ? ManifestSuffix.Length
            : remainder.EndsWith(PartExtension, StringComparison.Ordinal)
                ? PartExtension.Length
                : 0;
        if (suffixLength == 0)
            return remainder;

        var stem = remainder[..^suffixLength];
        if (!stem.EndsWith(EncryptionMarker, StringComparison.Ordinal))
            return remainder;

        isEncrypted = true;
        return string.Concat(stem[..^EncryptionMarker.Length], remainder[^suffixLength..]);
    }

    /// <summary>
    ///     A backup id comes from a cloud file name, which a user could craft through the web
    ///     dashboard. It is used in local staging paths, so only plain id characters are accepted.
    /// </summary>
    public static bool IsSafeBackupId(string? backupId) =>
        !string.IsNullOrWhiteSpace(backupId) && backupId.Length <= 64 &&
        !backupId.Contains("..", StringComparison.Ordinal) &&
        backupId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    /// <summary>Splits archive bytes into ordered parts of at most <paramref name="partBytes" /> bytes.</summary>
    public static IReadOnlyList<byte[]> Slice(byte[] archive, int partBytes = DefaultPartBytes)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (partBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(partBytes));
        if (archive.Length == 0)
            return [[]];

        var parts = new List<byte[]>((archive.Length + partBytes - 1) / partBytes);
        for (var offset = 0; offset < archive.Length; offset += partBytes)
        {
            var length = Math.Min(partBytes, archive.Length - offset);
            var part = new byte[length];
            Buffer.BlockCopy(archive, offset, part, 0, length);
            parts.Add(part);
        }

        return parts;
    }

    /// <summary>
    ///     Builds the manifest that describes the already-sliced parts of one backup.
    ///     <paramref name="payload" /> is the byte sequence the parts were sliced from: the v3 ZIP
    ///     archive itself for a plaintext package, or its ciphertext when <paramref name="encryption" />
    ///     is supplied. <see cref="CloudBackupManifest.ArchiveBytes" /> and
    ///     <see cref="CloudBackupManifest.ArchiveSha256" /> always describe that payload, so per-part
    ///     verification is unchanged by encryption.
    /// </summary>
    public static CloudBackupManifest BuildManifest(string backupId, IReadOnlyList<byte[]> parts, int partBytes,
        IReadOnlyList<string> roots, DateTime createdUtc, string producerVersion, byte[] payload,
        CloudBackupEncryption? encryption = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(payload);
        if (parts.Count == 0)
            throw new InvalidDataException("云端备份分片不能为空。");
        if (parts.Count > MaxPartCount)
            throw new InvalidDataException("云端备份分片数量超过限制。");
        if (partBytes is <= 0 or > DefaultPartBytes || payload.LongLength > MaxArchiveBytes ||
            parts.Any(part => part is null || part.LongLength > partBytes))
            throw new InvalidDataException("云端备份分片或归档长度无效。");

        var manifest = new CloudBackupManifest
        {
            Format = Format,
            SchemaVersion = encryption is null ? SchemaVersion : EncryptedSchemaVersion,
            ProducerVersion = producerVersion,
            BackupId = backupId,
            ArchiveName = BuildArchiveName(backupId),
            ArchiveBytes = payload.LongLength,
            ArchiveSha256 = Hash(payload),
            PartBytes = partBytes,
            CreatedUtc = createdUtc,
            Roots = [.. roots],
            Encryption = encryption,
            Parts = []
        };

        var encrypted = encryption is not null;
        for (var index = 0; index < parts.Count; index++)
        {
            manifest.Parts.Add(new CloudBackupPart
            {
                Index = index + 1,
                Name = BuildPartName(backupId, index + 1, parts.Count, encrypted),
                Length = parts[index].LongLength,
                Sha256 = Hash(parts[index])
            });
        }

        ValidateManifest(manifest);
        return manifest;
    }

    /// <summary>
    ///     Validates a downloaded manifest and rebuilds the payload the parts describe. Any structural
    ///     problem, missing/reordered part, or hash mismatch aborts before an import can start. An
    ///     encrypted package still has to be opened with <see cref="CloudBackupCipher" />-equivalent
    ///     key material afterwards; the GCM tag is what authenticates the plaintext.
    /// </summary>
    public static byte[] MergeAndVerify(CloudBackupManifest manifest, IReadOnlyList<byte[]> parts)
    {
        ValidateManifest(manifest);
        if (parts.Count != manifest.Parts.Count)
            throw new InvalidDataException("云端备份分片数量与清单不一致。");

        using var payload = new MemoryStream((int)manifest.ArchiveBytes);
        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var expected = manifest.Parts[index];
            var actual = parts[index];
            if (actual.LongLength != expected.Length || !Hash(actual).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"云端备份第 {expected.Index} 个分片校验失败。");
            payload.Write(actual);
        }

        var merged = payload.ToArray();
        if (merged.LongLength != manifest.ArchiveBytes ||
            !Hash(merged).Equals(manifest.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("云端备份归档校验失败。");
        return merged;
    }

    public static void ValidateManifest(CloudBackupManifest? manifest)
    {
        if (manifest is null || manifest.Format != Format ||
            manifest.SchemaVersion is not (SchemaVersion or EncryptedSchemaVersion))
            throw new InvalidDataException("云端备份清单格式不受支持。");
        if (manifest.Parts is null || !IsSafeBackupId(manifest.BackupId) ||
            !string.Equals(manifest.ArchiveName, BuildArchiveName(manifest.BackupId), StringComparison.Ordinal) ||
            manifest.Parts.Count is < 1 or > MaxPartCount)
            throw new InvalidDataException("云端备份清单内容无效。");
        if (manifest.PartBytes is <= 0 or > DefaultPartBytes)
            throw new InvalidDataException("云端备份清单的分片大小无效。");

        // The schema version, the encryption block, and the file names have to agree: the names carry
        // the listing hint a user sees, while the block is what a restore actually follows.
        if (manifest.SchemaVersion == EncryptedSchemaVersion)
            ValidateEncryption(manifest.Encryption);
        else if (manifest.Encryption is not null)
            throw new InvalidDataException("云端备份清单的加密信息与版本不一致。");

        var encrypted = manifest.Encryption is not null;
        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var part = manifest.Parts[index];
            if (part is null || part.Index != index + 1 || part.Length < 0 || part.Length > manifest.PartBytes ||
                !IsSha256(part.Sha256) ||
                !TryParseFileName(part.Name, out var partBackupId, out var partIndex, out var partCount,
                    out var isManifest, out var isPartEncrypted) ||
                isManifest || isPartEncrypted != encrypted ||
                partBackupId != manifest.BackupId || partIndex != part.Index || partCount != manifest.Parts.Count)
                throw new InvalidDataException("云端备份清单的分片描述无效。");
        }

        if (manifest.ArchiveBytes is < 0 or > MaxArchiveBytes || !IsSha256(manifest.ArchiveSha256))
            throw new InvalidDataException("云端备份清单的归档长度无效。");

        long declaredPartBytes = 0;
        foreach (var part in manifest.Parts)
        {
            if (part.Length > MaxArchiveBytes - declaredPartBytes)
                throw new InvalidDataException("云端备份清单的归档长度无效。");
            declaredPartBytes += part.Length;
        }

        if (declaredPartBytes != manifest.ArchiveBytes)
            throw new InvalidDataException("云端备份清单的归档长度无效。");
    }

    public static CloudBackupManifest DeserializeManifest(byte[] content)
    {
        try
        {
            return JsonSerializer.Deserialize<CloudBackupManifest>(content, ManifestJsonOptions)
                   ?? throw new InvalidDataException("云端备份清单为空。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("云端备份清单无法解析。", exception);
        }
    }

    public static byte[] SerializeManifest(CloudBackupManifest manifest) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(manifest, ManifestJsonOptions));

    /// <summary>Rejects a part size whose base64 JSON upload body would exceed the request-body budget.</summary>
    public static void EnsureUploadBodySize(int partBytes)
    {
        var base64Length = 4L * ((partBytes + 2) / 3);
        if (base64Length + JsonEnvelopeOverheadBytes > MaxUploadBodyBytes)
            throw new InvalidDataException("云端备份分片体积超过上传限制。");
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    /// <summary>
    ///     Validates the encryption block of an encrypted manifest. The parameters are bounded here
    ///     because Core owns the format: a manifest is downloaded from the account cloud, so it may
    ///     not be able to make a client spend an arbitrary amount of memory or time on key derivation.
    /// </summary>
    private static void ValidateEncryption(CloudBackupEncryption? encryption)
    {
        if (encryption is null)
            throw new InvalidDataException("云端备份清单缺少加密信息。");
        if (!string.Equals(encryption.Algorithm, CloudBackupEncryption.AesGcmAlgorithm, StringComparison.Ordinal) ||
            !string.Equals(encryption.Kdf, CloudBackupEncryption.Argon2idKdf, StringComparison.Ordinal) ||
            encryption.MemoryKiB is < CloudBackupEncryption.MinimumMemoryKiB or > CloudBackupEncryption.MaximumMemoryKiB ||
            encryption.Iterations is < CloudBackupEncryption.MinimumIterations or > CloudBackupEncryption.MaximumIterations ||
            encryption.Parallelism is < CloudBackupEncryption.MinimumParallelism or > CloudBackupEncryption.MaximumParallelism ||
            !IsBase64OfLength(encryption.Salt, CloudBackupEncryption.MinimumSaltBytes, CloudBackupEncryption.MaximumSaltBytes) ||
            !IsBase64OfLength(encryption.Nonce, CloudBackupEncryption.NonceBytes, CloudBackupEncryption.NonceBytes) ||
            !IsBase64OfLength(encryption.Tag, CloudBackupEncryption.TagBytes, CloudBackupEncryption.TagBytes) ||
            !IsBase64OfLength(encryption.Verifier, CloudBackupEncryption.VerifierBytes, CloudBackupEncryption.VerifierBytes))
            throw new InvalidDataException("云端备份清单的加密信息无效。");
    }

    private static bool IsBase64OfLength(string? value, int minimumBytes, int maximumBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length >= minimumBytes && bytes.Length <= maximumBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}

public sealed class CloudBackupManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = CloudBackupPackage.Format;
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = CloudBackupPackage.SchemaVersion;
    [JsonPropertyName("producer_version")] public string ProducerVersion { get; set; } = string.Empty;
    [JsonPropertyName("backup_id")] public string BackupId { get; set; } = string.Empty;
    [JsonPropertyName("archive_name")] public string ArchiveName { get; set; } = string.Empty;

    /// <summary>Length of the payload the parts carry: the v3 archive, or its ciphertext when encrypted.</summary>
    [JsonPropertyName("archive_bytes")] public long ArchiveBytes { get; set; }

    /// <summary>SHA-256 of the payload the parts carry, so it is the ciphertext hash for an encrypted package.</summary>
    [JsonPropertyName("archive_sha256")] public string ArchiveSha256 { get; set; } = string.Empty;

    [JsonPropertyName("part_bytes")] public int PartBytes { get; set; }
    [JsonPropertyName("created_utc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("roots")] public List<string> Roots { get; set; } = [];

    /// <summary>
    ///     Present only on <see cref="CloudBackupPackage.EncryptedSchemaVersion" /> manifests. Its
    ///     absence is what makes a package plaintext, so the block is authoritative for a restore.
    /// </summary>
    [JsonPropertyName("encryption")] public CloudBackupEncryption? Encryption { get; set; }

    [JsonPropertyName("parts")] public List<CloudBackupPart> Parts { get; set; } = [];
}

/// <summary>
///     Key derivation and authenticated-encryption parameters of one encrypted cloud backup. Everything
///     here is public by design (it travels in a cleartext manifest): the salt is what lets a second
///     signed-in machine derive the same key from the same passphrase, and the verifier is what lets it
///     check that passphrase from the small manifest alone, before downloading any part.
/// </summary>
public sealed class CloudBackupEncryption
{
    public const string AesGcmAlgorithm = "aes-256-gcm";
    public const string Argon2idKdf = "argon2id";

    public const int KeyBytes = 32;
    public const int MinimumSaltBytes = 16;
    public const int MaximumSaltBytes = 64;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;
    public const int VerifierBytes = 32;

    public const int MinimumMemoryKiB = 8 * 1024;
    public const int DefaultMemoryKiB = 64 * 1024;
    public const int MaximumMemoryKiB = 1024 * 1024;
    public const int MinimumIterations = 1;
    public const int DefaultIterations = 3;
    public const int MaximumIterations = 100;
    public const int MinimumParallelism = 1;
    public const int DefaultParallelism = 1;
    public const int MaximumParallelism = 16;

    [JsonPropertyName("algorithm")] public string Algorithm { get; set; } = AesGcmAlgorithm;
    [JsonPropertyName("kdf")] public string Kdf { get; set; } = Argon2idKdf;
    [JsonPropertyName("salt")] public string Salt { get; set; } = string.Empty;
    [JsonPropertyName("memory_kib")] public int MemoryKiB { get; set; } = DefaultMemoryKiB;
    [JsonPropertyName("iterations")] public int Iterations { get; set; } = DefaultIterations;
    [JsonPropertyName("parallelism")] public int Parallelism { get; set; } = DefaultParallelism;
    [JsonPropertyName("nonce")] public string Nonce { get; set; } = string.Empty;
    [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
    [JsonPropertyName("verifier")] public string Verifier { get; set; } = string.Empty;
}

public sealed class CloudBackupPart
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}
