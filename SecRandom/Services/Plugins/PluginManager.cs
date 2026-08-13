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

    private readonly List<PluginInfo> _plugins = [];
    private readonly Dictionary<string, PluginLoadContext> _loadContexts = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PluginInfo> Plugins => _plugins;

    string IPluginManager.PluginsDirectory => PluginsDirectory;

    public static string PluginsDirectory => Utils.GetDirectoryPath("plugins");
    public static string PluginPackagesDirectory => Utils.GetDirectoryPath("cache", "plugin-packages");
    public static string PluginConfigsDirectory => Utils.GetDirectoryPath("config", "plugins");

    public void Initialize(HostBuilderContext context, IServiceCollection services)
    {
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

        var disabledMarkerPath = Path.Combine(plugin.PluginFolderPath, ".disabled");
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

    private void ProcessPendingPackages()
    {
        foreach (var packagePath in Directory.EnumerateFiles(PluginPackagesDirectory, "*" + PluginPackageExtension))
        {
            try
            {
                PluginManifest manifest;
                using (var package = ZipFile.OpenRead(packagePath))
                    manifest = ReadManifest(package);
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

                    ValidateManifest(ReadManifest(File.ReadAllText(stagedManifestPath)));
                    if (Directory.Exists(targetPath))
                        Directory.Delete(targetPath, recursive: true);
                    Directory.Move(stagingPath, targetPath);
                    File.Delete(packagePath);
                }
                finally
                {
                    if (Directory.Exists(stagingPath))
                        Directory.Delete(stagingPath, recursive: true);
                }
            }
            catch (Exception)
            {
                // Keep a package that cannot be processed so it can be replaced or inspected by the user.
            }
        }
    }

    private IEnumerable<DiscoveredPlugin> DiscoverPlugins()
    {
        foreach (var directory in Directory.EnumerateDirectories(PluginsDirectory))
        {
            PluginInfo info;
            try
            {
                var manifestPath = Path.Combine(directory, PluginManifestFileName);
                if (!File.Exists(manifestPath))
                    continue;

                var manifest = ReadManifest(File.ReadAllText(manifestPath));
                ValidateManifest(manifest);
                info = new PluginInfo
                {
                    Manifest = manifest,
                    PluginFolderPath = Path.GetFullPath(directory),
                    PluginConfigFolder = Path.Combine(PluginConfigsDirectory, manifest.Id),
                    IsEnabled = !File.Exists(Path.Combine(directory, ".disabled"))
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
        plugin.Info.LoadStatus = PluginLoadStatus.Loaded;
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

    private static PluginManifest ReadManifest(ZipArchive package)
    {
        var entry = package.GetEntry(PluginManifestFileName)
                     ?? throw new InvalidDataException("Plugin package does not contain manifest.yml.");
        using var reader = new StreamReader(entry.Open());
        return ReadManifest(reader.ReadToEnd());
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
            entry.ExtractToFile(destinationPath, overwrite: true);
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

        if (!Version.TryParse(manifest.ApiVersion, out var apiVersion) || apiVersion.Major != 1)
            throw new InvalidDataException("Plugin API version 1.x is required.");
    }

    private sealed record DiscoveredPlugin(PluginInfo Info, bool IsEnabled);
}
