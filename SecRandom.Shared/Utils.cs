using System.ComponentModel;

namespace SecRandom.Shared;

public static class Utils
{
    public const string PackageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
    private const string ConfigDirectoryName = "config";
    private const string UnixHiddenEntriesFileName = ".hidden";
    private static readonly object DataRootGate = new();
    private static string? _configuredDataRoot;
    private static bool _dataRootWasRead;

    public static string PackageRoot => ResolvePackageRoot();
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

    private static string ResolvePackageRoot()
    {
        var appDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var configuredRoot = Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable);
        if (IsPortablePackageRoot(configuredRoot, appDirectory))
            return Path.GetFullPath(configuredRoot!);

        var parent = Directory.GetParent(appDirectory)?.FullName;
        return IsPortablePackageRoot(parent, appDirectory) ? Path.GetFullPath(parent!) : appDirectory;
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
        }
    }

}
