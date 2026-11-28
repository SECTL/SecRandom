using System.ComponentModel;
using SecRandom.Shared.DataRoot;

namespace SecRandom.Shared;

public static class Utils
{
    public const string PackageRootEnvironmentVariable = "SECRANDOM_PACKAGE_ROOT";
    // Keep this a literal: the compiler rejects the cross-type const reference in this
    // multi-targeted assembly (CS0133), while the wire value must stay identical to
    // DataRootCommandLine.DataRootEnvironmentVariable.
    public const string DataRootEnvironmentVariable = "SECRANDOM_DATA_ROOT";
    private const string ConfigDirectoryName = "config";
    private const string UnixHiddenEntriesFileName = ".hidden";
    private static readonly object DataRootGate = new();
    private static string? _configuredDataRoot;
    private static bool _dataRootWasRead;
    private static DesktopDataRootPreparationResult? _desktopPreparation;
    private static DataRootMode _dataRootMode = DataRootMode.Package;
    private static DataRootSource _dataRootSource = DataRootSource.Unknown;
    private static string? _anchorPath;
    private static IReadOnlyList<string> _otherDataRoots = [];
    private static bool _isPortablePackage;
    private static bool _needsMigrationNotice;

    public readonly record struct DesktopDataRootPreparationResult(
        bool IsPortablePackage,
        bool IsWritable,
        string DataRoot,
        string? ErrorMessage,
        DataRootMode Mode = DataRootMode.Package,
        DataRootSource Source = DataRootSource.Unknown,
        string? AnchorPath = null,
        IReadOnlyList<string>? OtherDataRoots = null,
        bool NeedsMigrationNotice = false);

    /// <summary>The mode the current data root was chosen in.</summary>
    public static DataRootMode DataRootMode => _dataRootMode;

    /// <summary>Why the current data root won; diagnostic and storage-page metadata, not a policy switch.</summary>
    public static DataRootSource DataRootSource => _dataRootSource;

    /// <summary>Anchor file that persists the current mode and root; <see langword="null" /> before preparation.</summary>
    public static string? AnchorPath => _anchorPath;

    /// <summary>Other known data locations that still hold data and were deliberately not merged.</summary>
    public static IReadOnlyList<string> OtherDataRoots => _otherDataRoots;

    /// <summary>True when the running payload belongs to a portable ZIP package.</summary>
    public static bool IsPortablePackage => _isPortablePackage;

    /// <summary>True when the current root differs from the location this mode would create on a fresh install.</summary>
    public static bool NeedsMigrationNotice => _needsMigrationNotice;

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
    ///     Resolves and fixates the desktop data root. The resolution order lives in
    ///     <see cref="DesktopDataRootResolver" />; this method only turns the pure result into the process-wide root:
    ///     it repairs payload-local data, probes writability, and persists the anchor when the choice is worth keeping.
    /// </summary>
    public static DesktopDataRootPreparationResult PrepareDesktopDataRoot(
        bool isDevelopmentBuild,
        string? commandLineDataRoot = null)
    {
        return PrepareDesktopDataRoot(new DesktopDataRootOptions
        {
            AppDirectory = Path.GetFullPath(AppContext.BaseDirectory),
            PackageRoot = Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable),
            CommandLineDataRoot = commandLineDataRoot,
            EnvironmentDataRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable),
            IsDevelopmentBuild = isDevelopmentBuild
        });
    }

    public static DesktopDataRootPreparationResult PrepareDesktopDataRoot(DesktopDataRootOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (DataRootGate)
        {
            if (_desktopPreparation is { } prepared)
                return prepared;
            if (_dataRootWasRead)
                throw new InvalidOperationException("The data root must be prepared before it is first used.");
            if (_configuredDataRoot is not null)
                throw new InvalidOperationException("The data root has already been configured.");

            var resolution = DesktopDataRootResolver.Resolve(options);
            var payloadDataRootToRepair = resolution.PayloadDataRootToRepair;
            var repairError = payloadDataRootToRepair is { } payloadDataRoot
                ? RepairPayloadDataRoot(payloadDataRoot, resolution.DataRoot)
                : null;
            if (repairError is not null)
            {
                // The repair did not finish, so the payload copy stays authoritative for this run: letting an
                // update replace it is bad, but pointing the app at a half-copied directory is worse.
                resolution = resolution with
                {
                    DataRoot = Path.GetFullPath(payloadDataRootToRepair!),
                    PayloadDataRootToRepair = null
                };
            }

            var dataRoot = Path.GetFullPath(resolution.DataRoot);
            var writable = DataRootPaths.TryVerifyWritable(dataRoot, out var writeError);
            var otherDataRoots = resolution.OtherDataRoots
                .Where(candidate => !DataRootPaths.PathEquals(candidate, dataRoot))
                .ToArray();

            _configuredDataRoot = dataRoot;
            _dataRootWasRead = true;
            _dataRootMode = resolution.Mode;
            _dataRootSource = resolution.Source;
            _anchorPath = resolution.AnchorPath;
            _otherDataRoots = otherDataRoots;
            _isPortablePackage = resolution.IsPortablePackage;
            _needsMigrationNotice = resolution.NeedsMigrationNotice;

            if (writable && ShouldPersistAnchor(resolution, options))
                DataRootAnchorStore.TryPersist(resolution with { DataRoot = dataRoot }, write: true);

            var result = new DesktopDataRootPreparationResult(
                resolution.IsPortablePackage,
                writable,
                dataRoot,
                writeError ?? repairError,
                resolution.Mode,
                resolution.Source,
                resolution.AnchorPath,
                otherDataRoots,
                resolution.NeedsMigrationNotice);
            _desktopPreparation = result;
            return result;
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
        return DesktopDataRootResolver.ResolvePackageRoot(
            Path.GetFullPath(AppContext.BaseDirectory),
            Environment.GetEnvironmentVariable(PackageRootEnvironmentVariable));
    }

    /// <summary>An integration entry that outlives this build must only point at the launcher that accepts it.</summary>
    private static bool IsPortablePackageRoot(string? candidate, string appDirectory)
    {
        return DataRootPaths.IsPortablePackageLayout(candidate!, appDirectory);
    }

    private static bool HasPortablePayloadKind(string appDirectory)
    {
        return DataRootPaths.IsPortablePackageKind(DataRootPaths.ReadPackageKind(appDirectory));
    }

    private static bool ShouldPersistAnchor(DesktopDataRootResolution resolution, DesktopDataRootOptions options)
    {
        // Explicit overrides are one-shot by design: a --data-root run must not silently become the permanent root.
        if (resolution.Source is DataRootSource.CommandLine or DataRootSource.Environment)
            return false;

        // Development builds keep data beside the payload and otherwise litter bin/Debug with an anchor.
        if (options.IsDevelopmentBuild && resolution.Mode == DataRootMode.Package)
            return false;

        return !string.IsNullOrWhiteSpace(resolution.AnchorPath);
    }

    /// <summary>
    ///     Moves data an older layout left inside a versioned payload directory out to the stable root.
    ///     Copy-then-delete keeps the payload copy intact when the copy fails halfway.
    /// </summary>
    private static string? RepairPayloadDataRoot(string payloadDataRoot, string dataRoot)
    {
        var source = Path.GetFullPath(payloadDataRoot);
        var target = Path.GetFullPath(dataRoot);
        if (DataRootPaths.PathEquals(source, target))
            return null;

        var copied = new List<string>();
        try
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                var directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                if (File.Exists(destination))
                    continue;

                File.Copy(file, destination);
                copied.Add(file);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                           NotSupportedException)
        {
            return exception.Message;
        }

        foreach (var file in copied)
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The payload copy is replaced by the next update anyway; leaving it only costs disk space.
            }
        }

        try
        {
            if (!Directory.EnumerateFileSystemEntries(source).Any())
                Directory.Delete(source);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is cosmetic; the target root already holds every file.
        }

        return null;
    }

    internal static void ResetDataRootForTests()
    {
        lock (DataRootGate)
        {
            _configuredDataRoot = null;
            _dataRootWasRead = false;
            _desktopPreparation = null;
            _dataRootMode = DataRootMode.Package;
            _dataRootSource = DataRootSource.Unknown;
            _anchorPath = null;
            _otherDataRoots = [];
            _isPortablePackage = false;
            _needsMigrationNotice = false;
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

    /// <summary>Suffix of a student-list or prize-pool SQLite database.</summary>
    public const string RosterDatabaseExtension = ".srl";

    /// <summary>Suffix of the single SQLite store containing all orchestration profiles and cursors.</summary>
    public const string OrchestrationDatabaseExtension = ".sro";

    /// <summary>Suffix of the SQLite database containing fullscreen notification text libraries.</summary>
    public const string TextLibraryDatabaseExtension = ".srtlib";

    /// <summary>Suffix of an intermediate per-profile roster database after consolidation.</summary>
    public const string RosterDatabaseMigratedExtension = ".srl.migrated";

    /// <summary>Suffix of a legacy roster JSON retained after successful migration.</summary>
    public const string RosterLegacyBackupExtension = ".json.migrated";
}
