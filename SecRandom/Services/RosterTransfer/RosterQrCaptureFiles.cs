namespace SecRandom.Services.RosterTransfer;

/// <summary>
///     Deletes the capture files a native camera backend leaves behind for QR roster scanning.
/// </summary>
/// <remarks>
///     CameraView's Android provider writes one full-resolution JPEG under the app's external files
///     directory for every shutter release and never deletes it. Only the backend's own file-name shape is
///     removed, and only directly inside the directories the platform head hands over, so application data
///     stored below the same root stays intact.
/// </remarks>
public static class RosterQrCaptureFiles
{
    /// <summary>File-name prefix the camera backend uses for its capture files.</summary>
    public const string CaptureFilePrefix = "photo_";

    /// <summary>File-name extension the camera backend uses for its capture files.</summary>
    public const string CaptureFileExtension = ".jpg";

    /// <summary>Enumeration pre-filter for <see cref="CaptureFilePrefix" />; the exact name is checked afterwards.</summary>
    public const string CaptureSearchPattern = CaptureFilePrefix + "*";

    /// <summary>Deletes the supplied directories' capture files that are at least <paramref name="minimumAge" /> old.</summary>
    /// <param name="minimumAge">
    ///     A grace window for a capture the backend may still have to read. <see cref="TimeSpan.Zero" /> deletes
    ///     every capture file, including ones just written.
    /// </param>
    /// <param name="directories">Directories to clean; blank or missing entries are ignored.</param>
    /// <returns>The number of bytes reclaimed.</returns>
    public static long Clear(TimeSpan minimumAge, params string?[] directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        var cutoffUtc = minimumAge > TimeSpan.Zero ? DateTime.UtcNow - minimumAge : DateTime.MaxValue;
        long reclaimed = 0;
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            try
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (var file in Directory.EnumerateFiles(directory, CaptureSearchPattern,
                             SearchOption.TopDirectoryOnly))
                {
                    if (!IsCaptureFile(Path.GetFileName(file)))
                        continue;

                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTimeUtc > cutoffUtc)
                            continue;

                        var length = info.Length;
                        File.Delete(file);
                        reclaimed += length;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // A capture the backend is still writing stays until the next cleanup.
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // An unavailable directory costs only this cleanup pass.
            }
        }

        return reclaimed;
    }

    private static bool IsCaptureFile(string fileName) =>
        fileName.StartsWith(CaptureFilePrefix, StringComparison.Ordinal) &&
        fileName.EndsWith(CaptureFileExtension, StringComparison.OrdinalIgnoreCase);
}
