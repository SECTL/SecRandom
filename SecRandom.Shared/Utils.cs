using System.ComponentModel;

namespace SecRandom.Shared;

public static class Utils
{
    public const string PackageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
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
    ///     Selects an application-private data root before any persisted data path is resolved.
    /// </summary>
    internal static void ConfigureDataRoot(string dataRoot)
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
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

        return path;
    }

    public static string GetDirectoryPath([Localizable(false)] params string[] strings)
    {
        var path = GetPath(strings);

        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path)) Directory.CreateDirectory(path);

        return path;
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
