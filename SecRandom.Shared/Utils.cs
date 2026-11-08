using System.ComponentModel;
using System.Text.Json;

namespace SecRandom.Shared;

public static class Utils
{
    public const string PackageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
    private const string ConfigDirectoryName = "config";
    private const string UnixHiddenEntriesFileName = ".hidden";
    private static readonly object DataRootGate = new();
    private static string? _configuredDataRoot;
    private static bool _dataRootWasRead;
    private static DesktopDataRootPreparationResult? _desktopPreparation;

    public readonly record struct DesktopDataRootPreparationResult(
        bool IsPortablePackage,
        bool IsWritable,
        string DataRoot,
        string? ErrorMessage);

    public static string PackageRoot => ResolvePackageRoot();

    /// <summary>
    ///     File name of the stable portable-package launcher kept beside the version directories.
    /// </summary>
    public static string LauncherFileName =>
        OperatingSystem.IsWindows() ? "SecRandomLauncher.exe" : "SecRandomLauncher";

    public static string DataRoot
    {
        get
        {
            lock (DataRootGate)
            {
                _dataRootWasRead = true;
                return _configuredDataRoot ?? Path.Combine(PackageRoot, "data");
            }
        }
    }

    /// <summary>
    ///     Selects the app-private data root before any persisted path is resolved on a mobile platform.
    /// </summary>
    public static void ConfigureMobileDataRoot()
    {
        ConfigureDataRoot(GetMobileDataRootPath());
    }

    /// <summary>
    ///     Resolves the fixed application-private mobile data directory without changing the configured root.
    /// </summary>
    public static string GetMobileDataRootPath()
    {
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            throw new PlatformNotSupportedException("Mobile data roots are only supported on Android and iOS.");

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The platform does not provide an application-private data directory.");

        return Path.Combine(localApplicationData, "SecRandom", "data");
    }

    /// <summary>
    ///     Checks the desktop package data directory before the first persisted path is used.
    ///     Installed packages fall back to LocalApplicationData; portable packages must remain
    ///     beside the executable so they can be moved as one unit.
    /// </summary>
    public static DesktopDataRootPreparationResult PrepareDesktopDataRoot()
    {
        var packageRoot = ResolvePackageRoot();
        return PrepareDesktopDataRoot(packageRoot, IsPortablePackage(packageRoot));
    }

    private static DesktopDataRootPreparationResult PrepareDesktopDataRoot(string packageRoot, bool isPortablePackage)
    {
        lock (DataRootGate)
        {
            if (_desktopPreparation is { } prepared)
                return prepared;
            if (_dataRootWasRead)
                throw new InvalidOperationException("The data root must be prepared before it is first used.");
            if (_configuredDataRoot is not null)
                throw new InvalidOperationException("The data root has already been configured.");

            var packageDataRoot = Path.Combine(packageRoot, "data");
            if (TryVerifyWritable(packageDataRoot, out var packageError))
            {
                _configuredDataRoot = Path.GetFullPath(packageDataRoot);
                _dataRootWasRead = true;
                var result = new DesktopDataRootPreparationResult(isPortablePackage, true, _configuredDataRoot, null);
                _desktopPreparation = result;
                return result;
            }

            if (isPortablePackage)
            {
                _configuredDataRoot = Path.GetFullPath(packageDataRoot);
                _dataRootWasRead = true;
                var result = new DesktopDataRootPreparationResult(true, false, _configuredDataRoot, packageError);
                _desktopPreparation = result;
                return result;
            }

            var fallbackRoot = GetDesktopFallbackDataRoot();
            if (!TryVerifyWritable(fallbackRoot, out var fallbackError))
            {
                throw new InvalidOperationException(
                    $"Neither the installed package data directory nor the per-user data directory is writable. " +
                    $"Package: {packageDataRoot}; fallback: {fallbackRoot}; {fallbackError}");
            }

            _configuredDataRoot = Path.GetFullPath(fallbackRoot);
            _dataRootWasRead = true;
            var fallbackResult = new DesktopDataRootPreparationResult(false, true, _configuredDataRoot, packageError);
            _desktopPreparation = fallbackResult;
            return fallbackResult;
        }
    }

    private static void ConfigureDataRoot(string dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            throw new ArgumentException("The data root cannot be blank.", nameof(dataRoot));

        var normalizedRoot = Path.GetFullPath(dataRoot);
        lock (DataRootGate)
        {
            if (_dataRootWasRead)
                throw new InvalidOperationException("The data root must be configured before it is first used.");
            if (_configuredDataRoot is not null)
                throw new InvalidOperationException("The data root has already been configured.");

            _configuredDataRoot = normalizedRoot;
        }
    }

    private static string GetPath([Localizable(false)] params string[] strings)
    {
        return Path.Combine([DataRoot, ..strings]);
    }

    public static string GetFilePath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            EnsureConfigDirectoryHidden(strings);
        }

        return path;
    }

    public static string GetDirectoryPath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        if (!string.IsNullOrEmpty(path))
        {
            Directory.CreateDirectory(path);
            EnsureConfigDirectoryHidden(strings);
        }

        return path;
    }

    private static void EnsureConfigDirectoryHidden(IReadOnlyList<string> pathSegments)
    {
        if (pathSegments.Count == 0
            || !string.Equals(pathSegments[0], ConfigDirectoryName, StringComparison.Ordinal))
            return;

        var configDirectory = Path.Combine(DataRoot, ConfigDirectoryName);
        try
        {
            var attributes = File.GetAttributes(configDirectory);
            File.SetAttributes(configDirectory, attributes | FileAttributes.Hidden | FileAttributes.System);
        }
        catch (PlatformNotSupportedException)
        {
            EnsureUnixHiddenEntry(configDirectory);
        }
        catch (IOException)
        {
            // A read/write path must remain usable when the filesystem cannot persist attributes.
        }
        catch (UnauthorizedAccessException)
        {
            // Hiding is best effort; it must not prevent config loading or saving.
        }

        if (!OperatingSystem.IsWindows())
            EnsureUnixHiddenEntry(configDirectory);
    }

    private static void EnsureUnixHiddenEntry(string configDirectory)
    {
        var dataDirectory = Directory.GetParent(configDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return;

        var hiddenEntriesPath = Path.Combine(dataDirectory, UnixHiddenEntriesFileName);
        try
        {
            var entries = File.Exists(hiddenEntriesPath)
                ? File.ReadAllLines(hiddenEntriesPath)
                : [];
            if (entries.Any(entry => string.Equals(entry.Trim(), ConfigDirectoryName, StringComparison.Ordinal)))
                return;

            using var stream = new FileStream(hiddenEntriesPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            if (stream.Length > 0)
                writer.WriteLine();
            writer.WriteLine(ConfigDirectoryName);
        }
        catch (IOException)
        {
            // Hidden metadata is optional on filesystems without a writable parent directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Hidden metadata is optional; retain normal config behavior when it is denied.
        }
    }

    /// <summary>
    ///     Resolves the launcher executable of the portable package this process runs from.
    ///     Installed packages have no launcher; callers then keep using the application executable
    ///     that lives beside this build.
    /// </summary>
    public static bool TryGetPortableLauncherPath(out string launcherPath)
    {
        return TryGetPortableLauncherPath(PackageRoot, AppContext.BaseDirectory, out launcherPath);
    }

    private static bool TryGetPortableLauncherPath(
        string? packageRoot,
        string? appBaseDirectory,
        out string launcherPath)
    {
        launcherPath = string.Empty;
        if (string.IsNullOrWhiteSpace(packageRoot) || string.IsNullOrWhiteSpace(appBaseDirectory))
            return false;

        var appDirectory = Path.GetFullPath(appBaseDirectory);

        // An integration entry that outlives this build must only point at the launcher when that
        // launcher will accept the payload beside it, so both the app-* package layout and the
        // portable-zip marker are required.
        if (!IsPortablePackageRoot(packageRoot, appDirectory) || !HasPortablePayloadKind(appDirectory))
            return false;

        var candidate = Path.Combine(Path.GetFullPath(packageRoot), LauncherFileName);
        if (!File.Exists(candidate))
            return false;

        launcherPath = candidate;
        return true;
    }

    private static string ResolvePackageRoot()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable);
        if (IsPortablePackageRoot(configuredRoot, appDirectory))
            return Path.GetFullPath(configuredRoot!);

        var parent = Directory.GetParent(appDirectory)?.FullName;
        return IsPortablePackageRoot(parent, appDirectory) ? Path.GetFullPath(parent!) : appDirectory;
    }

    private static string GetDesktopFallbackDataRoot()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            throw new InvalidOperationException("The platform does not provide a per-user data directory.");

        return Path.Combine(localApplicationData, "SecRandom", "data");
    }

    private static bool IsPortablePackage(string packageRoot)
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        return IsPortablePackageRoot(packageRoot, appDirectory)
               && HasPortablePayloadKind(appDirectory);
    }

    private static bool HasPortablePayloadKind(string appDirectory)
    {
        var markerPath = Path.Combine(appDirectory, "SecRandom.package.json");
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(markerPath));
            return marker.RootElement.TryGetProperty("packageKind", out var packageKind)
                   && string.Equals(packageKind.GetString(), "portable-zip", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryVerifyWritable(string directory, out string? errorMessage)
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
            try
            {
                if (File.Exists(testFile))
                    File.Delete(testFile);
            }
            catch (IOException)
            {
                // The original write error is more useful to the caller.
            }
            catch (UnauthorizedAccessException)
            {
                // The original write error is more useful to the caller.
            }

            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool IsPortablePackageRoot(string? candidate, string appDirectory)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Directory.Exists(candidate))
            return false;

        var root = Path.GetFullPath(candidate);
        var normalizedAppDirectory = Path.GetFullPath(appDirectory);
        var parent = Directory.GetParent(normalizedAppDirectory)?.FullName;
        if (!string.Equals(root, parent, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal))
            return false;

        var appDirectoryName = Path.GetFileName(normalizedAppDirectory.TrimEnd(Path.DirectorySeparatorChar));
        return appDirectoryName.StartsWith("app-", StringComparison.Ordinal)
               && File.Exists(Path.Combine(normalizedAppDirectory, "SecRandom.package.json"));
    }

    internal static void ResetDataRootForTests()
    {
        lock (DataRootGate)
        {
            _configuredDataRoot = null;
            _dataRootWasRead = false;
            _desktopPreparation = null;
        }
    }

    /// <summary>
    ///     Suffix of a profile history database. History records live in this file rather than in JSON, because a
    ///     large history was rewritten in full on every save; the suffix is deliberately not <c>.json</c> so the
    ///     JSON readers (archive validation, the pre-database snapshot reader) can never pick a database up as one.
    /// </summary>
    public const string HistoryDatabaseExtension = ".srhdb";

    /// <summary>
    ///     Suffix an untouched legacy history JSON is renamed to after its records have been imported into
    ///     <see cref="HistoryDatabaseExtension" />. It is kept for one-way rollback and is not read again, because the
    ///     extension no longer matches a <c>*.json</c> enumeration.
    /// </summary>
    public const string HistoryLegacyBackupExtension = ".json.migrated";
}
