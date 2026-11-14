using System.Runtime.InteropServices;

namespace SecRandom.Shared.DataRoot;

/// <summary>
///     Platform paths the data-root layer needs. These are pure path computations; nothing here creates a
///     directory or touches the file system.
/// </summary>
public static class DataRootPaths
{
    public const string AnchorFileName = "data-root.json";
    public const string JournalFileName = "data-root-journal.json";
    public const string MigrationLockFileName = "data-root-migration.lock";
    public const string PayloadMarkerFileName = "SecRandom.package.json";
    public const string PayloadDirectoryPrefix = "app-";
    public const string DataDirectoryName = "data";
    public const string ProductDirectoryName = "SecRandom";

    /// <summary>Per-user application data root the standard mode creates.</summary>
    public static string GetStandardDataRoot()
    {
        return Path.Combine(GetStandardAnchorDirectory(), DataDirectoryName);
    }

    /// <summary>Per-user directory that holds the anchor, journal, and migration lock for installed builds.</summary>
    public static string GetStandardAnchorDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(localAppData, ProductDirectoryName);

            var home = GetHomeDirectory();
            return Path.Combine(home, "AppData", "Local", ProductDirectoryName);
        }

        if (OperatingSystem.IsMacOS())
            return Path.Combine(GetHomeDirectory(), "Library", "Application Support", ProductDirectoryName);

        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrWhiteSpace(configHome)
            ? Path.Combine(GetHomeDirectory(), ".config", "secrandom")
            : Path.Combine(configHome, "secrandom");
    }

    /// <summary>Anchor file path: inside the portable package so it travels with the folder, else per-user.</summary>
    public static string GetAnchorPath(string packageRoot, bool isPortablePackage)
    {
        return Path.Combine(GetAnchorDirectory(packageRoot, isPortablePackage), AnchorFileName);
    }

    public static string GetJournalPath(string packageRoot, bool isPortablePackage)
    {
        return Path.Combine(GetAnchorDirectory(packageRoot, isPortablePackage), JournalFileName);
    }

    public static string GetMigrationLockPath(string packageRoot, bool isPortablePackage)
    {
        return Path.Combine(GetAnchorDirectory(packageRoot, isPortablePackage), MigrationLockFileName);
    }

    public static string GetAnchorDirectory(string packageRoot, bool isPortablePackage)
    {
        return isPortablePackage ? Path.GetFullPath(packageRoot) : GetStandardAnchorDirectory();
    }

    public static string GetHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        var env = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrWhiteSpace(env) ? Path.GetTempPath() : env;
    }

    /// <summary>Case sensitivity matches the filesystem convention of the running platform.</summary>
    public static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool PathEquals(string left, string right)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison);
    }

    /// <summary>True when <paramref name="candidate" /> equals <paramref name="parent" /> or lives underneath it.</summary>
    public static bool IsSameOrNested(string parent, string candidate)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (string.Equals(normalizedParent, normalizedCandidate, PathComparison))
            return true;

        return normalizedCandidate.StartsWith(
            normalizedParent + Path.DirectorySeparatorChar,
            PathComparison);
    }

    /// <summary>Whether this process runs from a portable package payload (used before the resolver runs).</summary>
    public static bool IsPortablePackageLayout(string packageRoot, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageRoot) || !Directory.Exists(packageRoot))
            return false;

        var normalizedAppDirectory = Path.GetFullPath(appDirectory);
        var parent = Directory.GetParent(normalizedAppDirectory)?.FullName;
        if (parent is null || !PathEquals(packageRoot, parent))
            return false;

        var appDirectoryName = Path.GetFileName(normalizedAppDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return appDirectoryName.StartsWith(PayloadDirectoryPrefix, StringComparison.Ordinal)
               && File.Exists(Path.Combine(normalizedAppDirectory, PayloadMarkerFileName));
    }

    /// <summary>Reads the package marker's <c>packageKind</c>, ignoring every malformed or missing marker.</summary>
    public static string? ReadPackageKind(string? payloadDirectory)
    {
        if (string.IsNullOrWhiteSpace(payloadDirectory))
            return null;

        var markerPath = Path.Combine(payloadDirectory, PayloadMarkerFileName);
        try
        {
            if (!File.Exists(markerPath))
                return null;

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(markerPath));
            return document.RootElement.TryGetProperty("packageKind", out var kind)
                ? kind.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                             or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    public static bool IsPortablePackageKind(string? packageKind)
    {
        return string.Equals(packageKind, "portable-zip", StringComparison.Ordinal);
    }

    /// <summary>Enumerates payload directories beside the package root (<c>app-*</c>).</summary>
    public static IReadOnlyList<string> EnumeratePayloadDirectories(string packageRoot)
    {
        try
        {
            if (!Directory.Exists(packageRoot))
                return [];

            return Directory.EnumerateDirectories(packageRoot, PayloadDirectoryPrefix + "*")
                .Where(directory => File.Exists(Path.Combine(directory, PayloadMarkerFileName)))
                .OrderBy(directory => directory, StringComparer.Ordinal)
                .Select(Path.GetFullPath)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                             or DirectoryNotFoundException)
        {
            return [];
        }
    }

    /// <summary>True when the directory exists and holds at least one entry.</summary>
    public static bool HasData(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return false;

        try
        {
            return Directory.EnumerateFileSystemEntries(directory).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Real write probe used before a root is accepted.</summary>
    public static bool TryVerifyWritable(string directory, out string? errorMessage)
    {
        var testFile = Path.Combine(directory, $".secrandom-write-test-{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.WriteByte(0);
                stream.Flush(true);
            }

            File.Delete(testFile);
            errorMessage = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException or ArgumentException)
        {
            TryDeleteFile(testFile);
            errorMessage = exception.Message;
            return false;
        }
    }

    public static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort: the caller's original error is more useful.
        }
    }

    /// <summary>Maps a directory onto the path separator style of the current platform.</summary>
    public static string Normalize(string path)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    internal static bool IsWindows => OperatingSystem.IsWindows();

    internal static bool IsMacOs => OperatingSystem.IsMacOS();

    internal static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
}