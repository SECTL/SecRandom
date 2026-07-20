using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Archive;

public enum ArchiveKind
{
    AllData,
    ManualBackup,
    AutomaticBackup,
    PreImportSettings,
    PreImportAllData,
    Diagnostic
}

public enum ArchiveFormat
{
    Current,
    Unknown
}

public sealed record ImportInspection(
    ArchiveFormat Format,
    ArchiveKind? Kind,
    string ProducerVersion,
    int FileCount,
    long UncompressedBytes,
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Warnings)
{
    public bool IsSupportedV3 => Format == ArchiveFormat.Current;
}

public sealed record ImportResult(
    string SnapshotPath,
    int ImportedFiles,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> PreservedLegacyFiles);

public sealed class ArchiveManifest
{
    [JsonPropertyName("format")] public string Format { get; set; } = "secrandom-archive";
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
    [JsonPropertyName("created_utc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("producer_version")] public string ProducerVersion { get; set; } = string.Empty;
    [JsonPropertyName("files")] public List<ArchiveFileEntry> Files { get; set; } = [];
}

public sealed class ArchiveFileEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = string.Empty;
    [JsonPropertyName("length")] public long Length { get; set; }
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = string.Empty;
}

public sealed class SettingsEnvelope
{
    [JsonPropertyName("format")] public string Format { get; set; } = "secrandom-settings";
    [JsonPropertyName("schema_version")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("created_utc")] public DateTime CreatedUtc { get; set; }
    [JsonPropertyName("producer_version")] public string ProducerVersion { get; set; } = string.Empty;
    [JsonPropertyName("settings")] public JsonElement Settings { get; set; }
}
