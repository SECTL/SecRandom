using System.IO.Compression;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecRandom.PluginSdk;
using SecRandom.Shared;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SecRandom.Services.Plugins;

public sealed class PluginManager : IPluginManager
{
    public const string PluginPackageExtension = ".srpx";
    public const string PluginManifestFileName = "manifest.yml";
    public const string UninstallMarkerFileName = ".uninstall";
    public const string DisabledMarkerFileName = ".disabled";
    private const long MaxPackageBytes = 64L * 1024 * 1024;
    private const int MaxPackageEntryCount = 1024;
    private const long MaxManifestBytes = 64L * 1024;
    private const long MaxEntryBytes = 64L * 1024 * 1024;
    private const long MaxTotalUncompressedBytes = 256L * 1024 * 1024;

    private readonly List<PluginInfo> _plugins = [];
    private readonly Dictionary<string, PluginLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PluginBase> _entrances = [];
    private readonly object _entranceGate = new();

    public IReadOnlyList<PluginInfo> Plugins => _plugins;

    string IPluginManager.PluginsDirectory => PluginsDirectory;

    public static string PluginsDirectory => Utils.GetDirectoryPath("plugins");
    public static string PluginPackagesDirectory => Utils.GetDirectoryPath("cache", "plugin-packages");
    public static string PluginConfigsDirectory => Utils.GetDirectoryPath("config", "plugins");

    private static IReadOnlyList<string> _startupExternalPluginDirectories = [];

    /// <summary>
    ///     Parses <c>--epp</c> / <c>--externalPluginPath</c> startup arguments (one value per flag, repeatable)
    ///     into the external plugin directories used by every PluginManager instance for this process.
    /// </summary>
    public static void SetStartupArguments(IReadOnlyList<string> args)
    {
        _startupExternalPluginDirectories = ParseExternalPluginDirectories(args);
    }

    internal static IReadOnlyList<string> ParseExternalPluginDirectories(IReadOnlyList<string> args)
    {
        List<string> directories = [];
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            if (!string.Equals(argument, "--epp", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(argument, "--externalPluginPath", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Count)
                continue;
            var value = args[++index];
            if (!string.IsNullOrWhiteSpace(value))
                directories.Add(value);
        }

        return directories;
    }

    /// <summary>
    ///     Additional plugin directories contributed by <c>--externalPluginPath</c> startup arguments
    ///     (also <c>--epp</c>). Development plugins are discovered from these directories and keep their
    ///     in-place layout; they are never moved or removed by the host.
    /// </summary>
    public IReadOnlyList<string> ExternalPluginDirectories { get; set; } = [];

    public void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        ExternalPluginDirectories = _startupExternalPluginDirectories;
        ProcessUninstallMarkers();
        ProcessPendingPackages();

        var discovered = DiscoverPlugins().ToList();
        foreach (var plugin in discovered)
            _plugins.Add(plugin.Info);

        var manifests = new Dictionary<string, DiscoveredPlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in discovered.Where(x => x.IsEnabled && x.Info.LoadStatus == PluginLoadStatus.NotLoaded))
        {
            if (!manifests.TryAdd(plugin.Info.Manifest.Id, plugin))
            {
                plugin.Info.LoadStatus = PluginLoadStatus.Error;
                plugin.Info.Exception = new InvalidOperationException(
                    $"The plugin id {plugin.Info.Manifest.Id} is already used by another plugin.");
            }
        }

        IReadOnlyList<DiscoveredPlugin> loadOrder;
        try
        {
            loadOrder = ResolveLoadOrder(manifests.Values);
        }
        catch (Exception exception)
        {
            foreach (var plugin in manifests.Values.Where(x => x.Info.LoadStatus == PluginLoadStatus.NotLoaded))
            {
                plugin.Info.LoadStatus = PluginLoadStatus.Error;
                plugin.Info.Exception = exception;
            }
            return;
        }

        foreach (var plugin in loadOrder)
        {
            if (plugin.Info.LoadStatus != PluginLoadStatus.NotLoaded)
                continue;

            var failedDependency = plugin.Info.Manifest.Dependencies.FirstOrDefault(dependency =>
                dependency.Required
                && _plugins.FirstOrDefault(candidate => string.Equals(
                    candidate.Manifest.Id,
                    dependency.Id,
                    StringComparison.OrdinalIgnoreCase)) is not { LoadStatus: PluginLoadStatus.Loaded });
            if (failedDependency is not null)
            {
                plugin.Info.LoadStatus = PluginLoadStatus.Error;
                plugin.Info.Exception = new InvalidOperationException(
                    $"Required plugin dependency {failedDependency.Id} did not load.");
                continue;
            }

            try
            {
                LoadPlugin(plugin, context, services);
            }
            catch (Exception exception)
            {
                plugin.Info.Exception = exception;
                plugin.Info.LoadStatus = PluginLoadStatus.Error;
            }
        }
    }

    public void StagePackage(string packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath)
            || !Path.GetExtension(packagePath).Equals(PluginPackageExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The selected file is not a SecRandom plugin package.");
        }

        using var package = ZipFile.OpenRead(packagePath);
        ValidatePackageBudget(packagePath, package);
        var manifest = ReadManifest(package);
        ValidateManifest(manifest);
        if (package.GetEntry(manifest.EntranceAssembly.Replace('\\', '/')) is null)
            throw new InvalidDataException("The plugin package does not contain its entrance assembly.");

        var destinationPath = Path.Combine(PluginPackagesDirectory, manifest.Id + PluginPackageExtension);
        var temporaryPath = destinationPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(packagePath, temporaryPath, overwrite: true);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public bool SetEnabled(string pluginId, bool enabled)
    {
        var plugin = _plugins.FirstOrDefault(candidate => string.Equals(
            candidate.Manifest.Id,
            pluginId,
            StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            return false;

        var disabledMarkerPath = Path.Combine(plugin.PluginFolderPath, DisabledMarkerFileName);
        if (enabled)
        {
            if (File.Exists(disabledMarkerPath))
                File.Delete(disabledMarkerPath);
        }
        else
        {
            File.WriteAllText(disabledMarkerPath, string.Empty);
        }

        plugin.IsEnabled = enabled;
        return true;
    }

    /// <summary>
    ///     Marks a plugin for uninstall. The plugin stays on disk until the next startup removes its folder;
    ///     its configuration directory under <c>data/config/plugins/&lt;id&gt;</c> is preserved.
    /// </summary>
    public bool UninstallPlugin(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(candidate => string.Equals(
            candidate.Manifest.Id,
            pluginId,
            StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            return false;

        try
        {
            File.WriteAllText(
                Path.Combine(plugin.PluginFolderPath, UninstallMarkerFileName),
                string.Empty);
        }
        catch
        {
            return false;
        }

        plugin.IsEnabled = false;
        return true;
    }

    /// <summary>
    ///     Disables a plugin whose code caused a process-level failure. Writes the same marker used by
    ///     the settings enable switch so the plugin stays disabled on the next startup.
    /// </summary>
    public bool DisablePluginOnCrash(string pluginId)
    {
        var plugin = _plugins.FirstOrDefault(candidate => string.Equals(
            candidate.Manifest.Id,
            pluginId,
            StringComparison.OrdinalIgnoreCase));
        if (plugin is null)
            return false;

        try
        {
            File.WriteAllText(
                Path.Combine(plugin.PluginFolderPath, DisabledMarkerFileName),
                string.Empty);
        }
        catch
        {
            return false;
        }

        plugin.IsEnabled = false;
        return true;
    }

    /// <summary>
    ///     Returns the id of the plugin whose load context owns the deepest stack frame of the exception,
    ///     or <see langword="null"/> when the failure is not plugin-originated.
    /// </summary>
    public string? GetPluginIdForException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            foreach (var (pluginId, loadContext) in _loadContexts)
            {
                if (loadContext.OwnsException(current))
                    return pluginId;
            }
        }

        return null;
    }

    public async ValueTask DisposePluginsAsync()
    {
        PluginBase[] entrances;
        lock (_entranceGate)
            entrances = _entrances.ToArray();
        foreach (var entrance in entrances)
        {
            try
            {
                await entrance.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Plugin disposal failed for {entrance.Info.Manifest.Id}: {exception}");
            }
        }
    }

    private void ProcessUninstallMarkers()
    {
        foreach (var directory in Directory.EnumerateDirectories(PluginsDirectory))
        {
            if (!File.Exists(Path.Combine(directory, UninstallMarkerFileName)))
                continue;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // Keep the marked folder so the user can inspect or retry uninstall later.
            }
        }
    }

    private void ProcessPendingPackages()
    {
        foreach (var packagePath in Directory.EnumerateFiles(PluginPackagesDirectory, "*" + PluginPackageExtension))
        {
            if (File.Exists(packagePath + ".rejected"))
                continue;

            try
            {
                PluginManifest manifest;
                using (var package = ZipFile.OpenRead(packagePath))
                {
                    ValidatePackageBudget(packagePath, package);
                    manifest = ReadManifest(package);
                }
                ValidateManifest(manifest);

                var targetPath = Path.Combine(PluginsDirectory, manifest.Id);
                var stagingPath = Path.Combine(PluginPackagesDirectory, $".staging-{Guid.NewGuid():N}");
                try
                {
                    Directory.CreateDirectory(stagingPath);
                    ExtractPackage(packagePath, stagingPath);
                    var stagedManifestPath = Path.Combine(stagingPath, PluginManifestFileName);
                    if (!File.Exists(stagedManifestPath))
                        throw new InvalidDataException("Plugin package does not contain manifest.yml at its root.");

                    ValidateManifest(ReadManifestFile(stagedManifestPath));
                    var backupPath = targetPath + ".replaced-" + Guid.NewGuid().ToString("N");
                    var hadExisting = Directory.Exists(targetPath);
                    if (hadExisting)
                        Directory.Move(targetPath, backupPath);
                    try
                    {
                        Directory.Move(stagingPath, targetPath);
                        File.Delete(packagePath);
                        if (Directory.Exists(backupPath))
                            Directory.Delete(backupPath, recursive: true);
                    }
                    catch
                    {
                        if (Directory.Exists(targetPath))
                            Directory.Delete(targetPath, recursive: true);
                        if (hadExisting && Directory.Exists(backupPath))
                            Directory.Move(backupPath, targetPath);
                        throw;
                    }
                }
                finally
                {
                    if (Directory.Exists(stagingPath))
                        Directory.Delete(stagingPath, recursive: true);
                }
            }
            catch (Exception exception)
            {
                if (exception is PluginPackageBudgetException)
                {
                    try
                    {
                        File.WriteAllText(packagePath + ".rejected", exception.Message);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    private IEnumerable<DiscoveredPlugin> DiscoverPlugins()
    {
        foreach (var directory in EnumeratePluginDirectories())
        {
            PluginInfo info;
            try
            {
                var manifestPath = Path.Combine(directory, PluginManifestFileName);
                if (!File.Exists(manifestPath))
                    continue;

                var manifest = ReadManifestFile(manifestPath);
                ValidateManifest(manifest);
                info = new PluginInfo
                {
                    Manifest = manifest,
                    PluginFolderPath = Path.GetFullPath(directory),
                    PluginConfigFolder = Path.Combine(PluginConfigsDirectory, manifest.Id),
                    IsEnabled = !File.Exists(Path.Combine(directory, DisabledMarkerFileName))
                };
            }
            catch (Exception exception)
            {
                info = new PluginInfo
                {
                    Manifest = new PluginManifest
                    {
                        Id = Path.GetFileName(directory),
                        Name = Path.GetFileName(directory)
                    },
                    PluginFolderPath = Path.GetFullPath(directory),
                    PluginConfigFolder = Path.Combine(PluginConfigsDirectory, Path.GetFileName(directory)),
                    IsEnabled = false,
                    LoadStatus = PluginLoadStatus.Error,
                    Exception = exception
                };
            }

            Directory.CreateDirectory(info.PluginConfigFolder);
            if (!info.IsEnabled && info.LoadStatus == PluginLoadStatus.NotLoaded)
                info.LoadStatus = PluginLoadStatus.Disabled;
            yield return new DiscoveredPlugin(info, info.IsEnabled);
        }
    }

    private IEnumerable<string> EnumeratePluginDirectories()
    {
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(PluginsDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(PluginsDirectory))
            {
                visited.Add(Path.GetFullPath(directory));
                yield return directory;
            }
        }

        foreach (var externalDirectory in ExternalPluginDirectories)
        {
            if (string.IsNullOrWhiteSpace(externalDirectory) || !Directory.Exists(externalDirectory))
                continue;

            foreach (var directory in Directory.EnumerateDirectories(externalDirectory))
            {
                var fullPath = Path.GetFullPath(directory);
                if (visited.Add(fullPath))
                    yield return directory;
            }
        }
    }

    private void LoadPlugin(DiscoveredPlugin plugin, HostBuilderContext context, IServiceCollection services)
    {
        var entrancePath = Path.GetFullPath(Path.Combine(
            plugin.Info.PluginFolderPath,
            plugin.Info.Manifest.EntranceAssembly));
        if (!File.Exists(entrancePath))
            throw new FileNotFoundException("Plugin entrance assembly was not found.", entrancePath);

        var dependencies = plugin.Info.Manifest.Dependencies
            .Where(dependency => _loadContexts.ContainsKey(dependency.Id))
            .Select(dependency => _loadContexts[dependency.Id])
            .ToList();
        var loadContext = new PluginLoadContext(entrancePath, dependencies);
        _loadContexts.Add(plugin.Info.Manifest.Id, loadContext);
        var assembly = loadContext.LoadFromAssemblyPath(entrancePath);
        var entranceType = assembly.ExportedTypes.FirstOrDefault(type =>
            typeof(PluginBase).IsAssignableFrom(type) && !type.IsAbstract);
        if (entranceType is null)
            throw new InvalidDataException("The entrance assembly does not contain a PluginBase implementation.");

        if (Activator.CreateInstance(entranceType) is not PluginBase entrance)
            throw new InvalidDataException("The plugin entry point could not be created.");

        entrance.Info = plugin.Info;
        entrance.PluginConfigFolder = plugin.Info.PluginConfigFolder;
        entrance.Initialize(context, services);
        services.AddSingleton<PluginBase>(entrance);
        services.AddSingleton(entranceType, entrance);
        lock (_entranceGate)
            _entrances.Add(entrance);
        plugin.Info.LoadStatus = PluginLoadStatus.Loaded;
    }

    private static PluginManifest ReadManifestFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var bounded = new BoundedReadStream(stream, MaxManifestBytes);
        using var reader = new StreamReader(bounded);
        var yaml = reader.ReadToEnd();
        if (bounded.Count > MaxManifestBytes)
            throw new InvalidDataException("Plugin manifest exceeds the resource budget.");
        return ReadManifest(yaml);
    }

    private static void ValidatePackageBudget(string packagePath, ZipArchive package)
    {
        var packageBytes = new FileInfo(packagePath).Length;
        ValidatePackageBudget(package, packageBytes, MaxPackageBytes, MaxPackageEntryCount, MaxManifestBytes, MaxEntryBytes, MaxTotalUncompressedBytes);
    }

    internal static void ValidatePackageBudgetForTests(
        ZipArchive package,
        long packageSize,
        long maxPackageBytes,
        int maxEntryCount,
        long maxManifestBytes,
        long maxEntryBytes,
        long maxTotalBytes)
        => ValidatePackageBudget(package, packageSize, maxPackageBytes, maxEntryCount, maxManifestBytes, maxEntryBytes, maxTotalBytes);

    private static void ValidatePackageBudget(
        ZipArchive package,
        long packageSize,
        long maxPackageBytes,
        int maxEntryCount,
        long maxManifestBytes,
        long maxEntryBytes,
        long maxTotalBytes)
    {
        if (packageSize > maxPackageBytes)
            throw new InvalidDataException("Plugin package exceeds the compressed size budget.");
        if (package.Entries.Count > maxEntryCount)
            throw new InvalidDataException("Plugin package contains too many entries.");

        long totalBytes = 0;
        foreach (var entry in package.Entries)
        {
            var limit = string.Equals(entry.FullName.Replace('\\', '/'), PluginManifestFileName, StringComparison.Ordinal)
                ? maxManifestBytes
                : maxEntryBytes;
            using var input = entry.Open();
            var count = CountStreamBytes(input, limit, ref totalBytes, maxTotalBytes);
            if (count > limit)
                throw new InvalidDataException("Plugin package entry exceeds the resource budget.");
        }
    }

    private static long CountStreamBytes(Stream input, long entryLimit, ref long totalBytes, long totalLimit)
    {
        var buffer = new byte[81920];
        long count = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            count += read;
            totalBytes += read;
            if (count > entryLimit || totalBytes > totalLimit)
                throw new InvalidDataException("Plugin package exceeds the uncompressed resource budget.");
        }
        return count;
    }

    private static IReadOnlyList<DiscoveredPlugin> ResolveLoadOrder(IEnumerable<DiscoveredPlugin> plugins)
    {
        var nodes = plugins.ToDictionary(x => x.Info.Manifest.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<DiscoveredPlugin>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in nodes.Values)
            Visit(plugin);

        return ordered;

        void Visit(DiscoveredPlugin plugin)
        {
            var id = plugin.Info.Manifest.Id;
            if (visited.Contains(id))
                return;
            if (!visiting.Add(id))
                throw new InvalidOperationException($"Circular plugin dependency detected at {id}.");

            foreach (var dependency in plugin.Info.Manifest.Dependencies)
            {
                if (!nodes.TryGetValue(dependency.Id, out var dependencyPlugin))
                {
                    if (dependency.Required)
                    {
                        plugin.Info.LoadStatus = PluginLoadStatus.Error;
                        plugin.Info.Exception = new InvalidOperationException(
                            $"Required plugin dependency {dependency.Id} was not found.");
                    }
                    continue;
                }

                Visit(dependencyPlugin);
                if (dependencyPlugin.Info.LoadStatus == PluginLoadStatus.Error && dependency.Required)
                {
                    plugin.Info.LoadStatus = PluginLoadStatus.Error;
                    plugin.Info.Exception = new InvalidOperationException(
                        $"Required plugin dependency {dependency.Id} failed to load.");
                }
            }

            visiting.Remove(id);
            visited.Add(id);
            if (plugin.Info.LoadStatus == PluginLoadStatus.NotLoaded)
                ordered.Add(plugin);
        }
    }

    private static void CopyWithBudget(Stream input, Stream output, long entryLimit, ref long totalBytes, long totalLimit)
    {
        var buffer = new byte[81920];
        long entryBytes = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            entryBytes += read;
            totalBytes += read;
            if (entryBytes > entryLimit || totalBytes > totalLimit)
                throw new InvalidDataException("Plugin package exceeds the uncompressed resource budget.");
            output.Write(buffer, 0, read);
        }
    }

    private sealed class PluginPackageBudgetException(string message) : Exception(message);

    private sealed class BoundedReadStream(Stream inner, long limit) : Stream
    {
        public long Count { get; private set; }
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowed = (int)Math.Min(count, limit - Count + 1);
            if (allowed <= 0)
            {
                var extra = inner.Read(buffer, offset, 1);
                Count += extra;
                return extra;
            }
            var read = inner.Read(buffer, offset, allowed);
            Count += read;
            return read;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var allowed = (int)Math.Min(buffer.Length, limit - Count + 1);
            if (allowed <= 0)
            {
                var extra = await inner.ReadAsync(buffer[..1], cancellationToken).ConfigureAwait(false);
                Count += extra;
                return extra;
            }
            var read = await inner.ReadAsync(buffer[..allowed], cancellationToken).ConfigureAwait(false);
            Count += read;
            return read;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private static PluginManifest ReadManifest(ZipArchive package)
    {
        var entry = package.GetEntry(PluginManifestFileName)
                     ?? throw new InvalidDataException("Plugin package does not contain manifest.yml.");
        using var input = entry.Open();
        using var bounded = new BoundedReadStream(input, MaxManifestBytes);
        using var reader = new StreamReader(bounded);
        var yaml = reader.ReadToEnd();
        if (bounded.Count > MaxManifestBytes)
            throw new InvalidDataException("Plugin manifest exceeds the resource budget.");
        return ReadManifest(yaml);
    }

    private static PluginManifest ReadManifest(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
        return deserializer.Deserialize<PluginManifest>(yaml)
               ?? throw new InvalidDataException("Plugin manifest is empty.");
    }

    private static void ExtractPackage(string packagePath, string targetPath)
    {
        var fullTargetPath = Path.GetFullPath(targetPath) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(packagePath);
        long totalBytes = 0;
        foreach (var entry in archive.Entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(targetPath, entry.FullName));
            if (!destinationPath.StartsWith(fullTargetPath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
                throw new InvalidDataException("Plugin package contains an entry outside its target directory.");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            using var input = entry.Open();
            using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            CopyWithBudget(input, output, MaxEntryBytes, ref totalBytes, MaxTotalUncompressedBytes);
        }
    }

    private static void ValidateManifest(PluginManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Id)
            || manifest.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || manifest.Id.Contains(Path.DirectorySeparatorChar)
            || manifest.Id.Contains(Path.AltDirectorySeparatorChar)
            || manifest.Id is "." or "..")
        {
            throw new InvalidDataException("Plugin id is invalid.");
        }

        if (string.IsNullOrWhiteSpace(manifest.EntranceAssembly)
            || Path.IsPathRooted(manifest.EntranceAssembly)
            || manifest.EntranceAssembly.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Plugin entrance assembly path is invalid.");
        }

        if (!Version.TryParse(manifest.ApiVersion, out var apiVersion) ||
            apiVersion.Major < PluginApiVersions.Current.Major)
        {
            throw new InvalidDataException(
                $"Plugin API version {manifest.ApiVersion} is not supported; " +
                $"{PluginApiVersions.Current.Major}.0 or higher is required.");
        }

        // permissions 是声明式策略：收敛成合法名字（去重、丢未知、大小写不敏感），
        // 未知名字只丢不拒——插件在旧宿主上声明了新权限不该导致装不上。
        manifest.Permissions = [.. PluginPermissionNames.Normalize(manifest.Permissions)];
    }

    private sealed record DiscoveredPlugin(PluginInfo Info, bool IsEnabled);
}
