using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Services.Archive;

/// <summary>
///     Shared ZIP entry helpers for SecRandom v3 archives. Platform layers (for example the
///     desktop diagnostic exporter) use these to emit entries that stay compatible with
///     <see cref="DataArchiveService" /> manifest validation.
/// </summary>
public static class ArchiveZipWriter
{
    public static void WriteTextEntry(ZipArchive archive, string path, string text, List<ArchiveFileEntry> files)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var entry = archive.CreateEntry(path, CompressionLevel.SmallestSize);
        using (var stream = entry.Open()) stream.Write(bytes);
        files.Add(new ArchiveFileEntry { Path = path, Length = bytes.Length, Sha256 = ComputeSha256Hex(bytes) });
    }

    public static void WriteManifest(ZipArchive archive, ArchiveKind kind, List<ArchiveFileEntry> files)
    {
        var manifest = new ArchiveManifest
        {
            Kind = kind.ToString(),
            CreatedUtc = DateTime.UtcNow,
            ProducerVersion = GlobalConstants.Version,
            Files = files
        };
        var entry = archive.CreateEntry("manifest.json", CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);
        writer.Write(System.Text.Json.JsonSerializer.Serialize(manifest, ConfigServiceBase.JsonOptions));
    }

    public static string ComputeSha256Hex(byte[] bytes)
    {
        return System.Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}
