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
    public const int SchemaVersion = 1;
    public const string FilePrefix = "SecRandom-cloud-";
    public const string PartExtension = ".srpart";
    public const string ManifestSuffix = "-manifest.json";

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

    public static string BuildPartName(string backupId, int index, int partCount) =>
        $"{FilePrefix}{backupId}-p{index:D2}of{partCount:D2}{PartExtension}";

    public static string BuildManifestName(string backupId) => $"{FilePrefix}{backupId}{ManifestSuffix}";

    public static string BuildArchiveName(string backupId) => $"SecRandom_cloud_{backupId}.zip";

    public static bool IsCloudBackupFileName(string? fileName) =>
        !string.IsNullOrWhiteSpace(fileName) && fileName.StartsWith(FilePrefix, StringComparison.Ordinal);

    /// <summary>
    ///     Parses a cloud file name produced by this package format. Foreign file names return false
    ///     so personal-cloud listings can keep unrelated platform files out of the backup list.
    /// </summary>
    public static bool TryParseFileName(string? fileName, out string backupId, out int partIndex, out int partCount, out bool isManifest)
    {
        backupId = string.Empty;
        partIndex = 0;
        partCount = 0;
        isManifest = false;
        if (!IsCloudBackupFileName(fileName))
            return false;

        var remainder = fileName![FilePrefix.Length..];
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

    /// <summary>Builds the manifest that describes the already-sliced parts of one backup.</summary>
    public static CloudBackupManifest BuildManifest(string backupId, IReadOnlyList<byte[]> parts, int partBytes,
        IReadOnlyList<string> roots, DateTime createdUtc, string producerVersion, byte[] archive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupId);
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(archive);
        if (parts.Count == 0)
            throw new InvalidDataException("云端备份分片不能为空。");
        if (parts.Count > MaxPartCount)
            throw new InvalidDataException("云端备份分片数量超过限制。");

        var manifest = new CloudBackupManifest
        {
            Format = Format,
            SchemaVersion = SchemaVersion,
            ProducerVersion = producerVersion,
            BackupId = backupId,
            ArchiveName = BuildArchiveName(backupId),
            ArchiveBytes = archive.LongLength,
            ArchiveSha256 = Hash(archive),
            PartBytes = partBytes,
            CreatedUtc = createdUtc,
            Roots = [.. roots],
            Parts = []
        };

        for (var index = 0; index < parts.Count; index++)
        {
            manifest.Parts.Add(new CloudBackupPart
            {
                Index = index + 1,
                Name = BuildPartName(backupId, index + 1, parts.Count),
                Length = parts[index].LongLength,
                Sha256 = Hash(parts[index])
            });
        }

        return manifest;
    }

    /// <summary>
    ///     Validates a downloaded manifest and rebuilds the archive. Any structural problem,
    ///     missing/reordered part, or hash mismatch aborts before an import can start.
    /// </summary>
    public static byte[] MergeAndVerify(CloudBackupManifest manifest, IReadOnlyList<byte[]> parts)
    {
        ValidateManifest(manifest);
        if (parts.Count != manifest.Parts.Count)
            throw new InvalidDataException("云端备份分片数量与清单不一致。");

        using var archive = new MemoryStream((int)manifest.ArchiveBytes);
        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var expected = manifest.Parts[index];
            var actual = parts[index];
            if (actual.LongLength != expected.Length || !Hash(actual).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"云端备份第 {expected.Index} 个分片校验失败。");
            archive.Write(actual);
        }

        var merged = archive.ToArray();
        if (merged.LongLength != manifest.ArchiveBytes ||
            !Hash(merged).Equals(manifest.ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("云端备份归档校验失败。");
        return merged;
    }

    public static void ValidateManifest(CloudBackupManifest? manifest)
    {
        if (manifest is null || manifest.Format != Format || manifest.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("云端备份清单格式不受支持。");
        if (!IsSafeBackupId(manifest.BackupId) || manifest.Parts.Count is < 1 or > MaxPartCount)
            throw new InvalidDataException("云端备份清单内容无效。");

        for (var index = 0; index < manifest.Parts.Count; index++)
        {
            var part = manifest.Parts[index];
            if (part.Index != index + 1 || part.Length < 0 ||
                !TryParseFileName(part.Name, out var partBackupId, out var partIndex, out var partCount, out var isManifest) ||
                isManifest || partBackupId != manifest.BackupId || partIndex != part.Index || partCount != manifest.Parts.Count)
                throw new InvalidDataException("云端备份清单的分片描述无效。");
        }

        if (manifest.ArchiveBytes < 0 || manifest.Parts.Sum(part => part.Length) != manifest.ArchiveBytes)
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

    public static string Hash(byte[] content) => Convert.ToHexString(SHA256.HashData(content));
}

public sealed class CloudBackupManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = CloudBackupPackage.Format;
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = CloudBackupPackage.SchemaVersion;
    [JsonPropertyName("producer_version")] public string ProducerVersion { get; set; } = string.Empty;
    [JsonPropertyName("backup_id")] public string BackupId { get; set; } = string.Empty;
    [JsonPropertyName("archive_name")] public string ArchiveName { get; set; } = string.Empty;
    [JsonPropertyName("archive_bytes")] public long ArchiveBytes { get; set; }
    [JsonPropertyName("archive_sha256")] public string ArchiveSha256 { get; set; } = string.Empty;
    [JsonPropertyName("part_bytes")] public int PartBytes { get; set; }
    [JsonPropertyName("created_utc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("roots")] public List<string> Roots { get; set; } = [];
    [JsonPropertyName("parts")] public List<CloudBackupPart> Parts { get; set; } = [];
}

public sealed class CloudBackupPart
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}
