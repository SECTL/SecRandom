using System.Text.Json;

namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Read/write the data-root anchor. The file is tiny, device-local, and must never be part of an archive,
///     so it is written with the same temp-file + atomic replace pattern the config handlers use.
/// </summary>
public static class DataRootAnchorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static DataRootAnchorReadResult Read(string anchorPath)
    {
        if (string.IsNullOrWhiteSpace(anchorPath) || !File.Exists(anchorPath))
            return new DataRootAnchorReadResult(DataRootAnchorStatus.Missing, null, null);

        try
        {
            var anchor = JsonSerializer.Deserialize<DataRootAnchor>(File.ReadAllText(anchorPath), JsonOptions);
            if (anchor is null)
                return new DataRootAnchorReadResult(DataRootAnchorStatus.Invalid, null, "The anchor file is empty.");

            if (anchor.SchemaVersion is < 1 or > DataRootAnchor.CurrentSchemaVersion)
                return new DataRootAnchorReadResult(
                    DataRootAnchorStatus.Invalid,
                    null,
                    $"Unsupported data-root anchor schema version {anchor.SchemaVersion}.");

            if (string.IsNullOrWhiteSpace(anchor.DataRoot))
                return new DataRootAnchorReadResult(
                    DataRootAnchorStatus.Invalid,
                    null,
                    "The anchor file does not name a data root.");

            anchor.DataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(anchor.DataRoot));
            anchor.Mode = DataRootModeExtensions.Parse(anchor.Mode).ToStorageValue();
            return new DataRootAnchorReadResult(DataRootAnchorStatus.Valid, anchor, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                             or JsonException or NotSupportedException or ArgumentException)
        {
            return new DataRootAnchorReadResult(
                DataRootAnchorStatus.Invalid,
                null,
                $"The data-root anchor could not be read: {exception.Message}");
        }
    }

    public static void Write(string anchorPath, DataRootAnchor anchor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(anchorPath);
        ArgumentNullException.ThrowIfNull(anchor);

        anchor.SchemaVersion = DataRootAnchor.CurrentSchemaVersion;
        anchor.UpdatedAtUtc = DateTimeOffset.UtcNow;

        var directory = Path.GetDirectoryName(Path.GetFullPath(anchorPath));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = anchorPath + ".tmp";
        var json = JsonSerializer.Serialize(anchor, JsonOptions);
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        File.Move(temporaryPath, anchorPath, overwrite: true);
    }

    /// <summary>Writes the anchor for a resolved root unless the resolution came from an explicit override.</summary>
    public static void TryPersist(DesktopDataRootResolution resolution, bool write)
    {
        if (!write || string.IsNullOrWhiteSpace(resolution.AnchorPath))
            return;

        try
        {
            Write(resolution.AnchorPath, new DataRootAnchor
            {
                Mode = resolution.Mode.ToStorageValue(),
                DataRoot = resolution.DataRoot,
                PackageKind = resolution.PackageKind
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                             or NotSupportedException or ArgumentException)
        {
            // Persisting the anchor is best effort: the resolved root still works for this run.
        }
    }
}