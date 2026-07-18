using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Draw;
using SecRandom.Core.Plugins;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Logging;
using SecRandom.Core.Views;
using SecRandom.Shared;
using SecRandom.Services.Security;
using SecRandom.Services.Linkage;

namespace SecRandom.Services.Plugins;

public sealed class PluginManagerService : IPluginManager
{
    public const string SupportedApiVersion = "1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<PluginManagerService> _logger;
    private readonly PluginStateStore _stateStore;
    private readonly List<PluginDescriptor> _plugins = [];
    private static readonly ConcurrentDictionary<string, (PluginStatus Status, string? Error)> StartupResults = [];

    public PluginManagerService(
        ILogger<PluginManagerService> logger,
        PluginStateStore stateStore)
    {
        _logger = logger;
        _stateStore = stateStore;
        Refresh();
    }

    public IReadOnlyList<PluginDescriptor> Plugins => new ReadOnlyCollection<PluginDescriptor>(_plugins);

    public static string PluginsDirectory => Utils.GetDirectoryPath("plugins");
    public static string PluginConfigsDirectory => Utils.GetDirectoryPath("configs", "plugins");

    public static void ConfigureEnabledPlugins(IServiceCollection services, PluginStateStore stateStore)
    {
        Directory.CreateDirectory(PluginsDirectory);
        var logger = NullLogger<PluginManagerService>.Instance;

        foreach (var descriptor in DiscoverPlugins(stateStore, logger).Where(x => x.IsEnabled && x.Status == PluginStatus.Discovered))
            ConfigurePlugin(services, descriptor, stateStore, logger);
    }

    public void Refresh()
    {
        Directory.CreateDirectory(PluginsDirectory);
        _plugins.Clear();

        foreach (var manifestPath in Directory.EnumerateFiles(PluginsDirectory, "plugin.json", SearchOption.AllDirectories))
            _plugins.Add(ReadDescriptor(manifestPath));

        _logger.LogInformation("已扫描插件目录：插件数量={Count}", _plugins.Count);
    }

    public void SetEnabled(string pluginId, bool isEnabled)
    {
        var state = _stateStore.GetOrCreate(pluginId);
        state.IsEnabled = isEnabled;
        state.RequiresRestart = true;
        _stateStore.Save();
        Refresh();
    }

    public string ImportPluginDirectory(string sourceDirectory)
    {
        var resolvedSource = Path.GetFullPath(sourceDirectory);
        if (!Directory.Exists(resolvedSource))
            throw new PluginImportException(PluginImportFailureReason.InvalidFolder, "Plugin directory does not exist.");

        var manifestPath = Path.Combine(resolvedSource, "plugin.json");
        if (!File.Exists(manifestPath))
            throw new PluginImportException(PluginImportFailureReason.InvalidManifest, "plugin.json was not found.");

        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions)
                       ?? throw new PluginImportException(PluginImportFailureReason.InvalidManifest, "Plugin manifest is empty.");

        ValidateManifest(manifest, resolvedSource, out var error, allowMissingAssembly: true);
        if (!string.IsNullOrWhiteSpace(error))
            throw new PluginImportException(PluginImportFailureReason.InvalidManifest, error);

        var targetDirectory = Path.Combine(PluginsDirectory, manifest.Id);
        if (Directory.Exists(targetDirectory))
            throw new PluginImportException(PluginImportFailureReason.AlreadyExists, $"Plugin '{manifest.Id}' already exists.");

        try
        {
            DirectoryCopy(resolvedSource, targetDirectory);
        }
        catch (Exception ex)
        {
            throw new PluginImportException(PluginImportFailureReason.CopyFailed, "Failed to copy plugin files.", ex);
        }

        Refresh();
        return manifest.Id;
    }

    public string ImportPluginPackage(string packagePath)
    {
        var resolvedPackage = Path.GetFullPath(packagePath);
        if (!File.Exists(resolvedPackage))
            throw new PluginImportException(PluginImportFailureReason.InvalidPackage, "Plugin package does not exist.");

        var tempDirectory = Path.Combine(Path.GetTempPath(), "SecRandomPluginImport", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            ZipFile.ExtractToDirectory(resolvedPackage, tempDirectory);
            var sourceDirectory = ResolveExtractedPluginDirectory(tempDirectory);
            return ImportPluginDirectory(sourceDirectory);
        }
        catch (InvalidDataException ex)
        {
            throw new PluginImportException(PluginImportFailureReason.InvalidPackage, "Plugin package is not a valid archive.", ex);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "清理插件导入临时目录失败：目录={TempDirectory}", tempDirectory);
            }
        }
    }

    public IEnumerable<PluginLogEntry> GetPluginLogs(string pluginId, int maxEntries = 500)
    {
        var prefix = GetPluginLogCategoryPrefix(pluginId);
        var current = Directory.EnumerateFiles(FileLoggerProvider.LogDirectory)
            .Where(path => path.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTime)
            .FirstOrDefault();

        if (current == null)
            return [];

        return ParseLogEntries(ReadTailLines(current, Math.Max(maxEntries * 5, 500)))
            .Where(entry => entry.Category.StartsWith(prefix, StringComparison.Ordinal))
            .TakeLast(maxEntries)
            .ToList();
    }

    public static string GetPluginLogCategoryPrefix(string pluginId)
    {
        return $"SecRandom.Plugin[{pluginId}].";
    }

    internal static void SetStartupResult(string pluginId, PluginStatus status, string? error)
    {
        StartupResults[pluginId] = (status, error);
    }

    private PluginDescriptor ReadDescriptor(string manifestPath)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions)
                           ?? throw new InvalidOperationException("Plugin manifest is empty.");

            var state = _stateStore.GetOrCreate(manifest.Id);
            var status = ValidateManifest(manifest, Path.GetDirectoryName(manifestPath)!, out var error);
            if (!state.IsEnabled && status == PluginStatus.Discovered)
                status = PluginStatus.Disabled;

            if (state.RequiresRestart)
                status = PluginStatus.PendingRestart;

            var startupResult = StartupResults.GetValueOrDefault(manifest.Id);
            if (state.IsEnabled && startupResult.Status != default)
            {
                status = startupResult.Status;
                error = startupResult.Error;
            }

            return new PluginDescriptor
            {
                Manifest = manifest,
                DirectoryPath = Path.GetDirectoryName(manifestPath)!,
                IsEnabled = state.IsEnabled,
                RequiresRestart = state.RequiresRestart,
                Status = status,
                ErrorMessage = error
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取插件清单失败：文件={ManifestPath}", manifestPath);
            return new PluginDescriptor
            {
                Manifest = new PluginManifest
                {
                    Id = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "unknown",
                    Name = Path.GetFileName(Path.GetDirectoryName(manifestPath)) ?? "unknown"
                },
                DirectoryPath = Path.GetDirectoryName(manifestPath)!,
                Status = PluginStatus.LoadFailed,
                ErrorMessage = ex.Message
            };
        }
    }

    private static IReadOnlyList<PluginDescriptor> DiscoverPlugins(PluginStateStore stateStore, ILogger logger)
    {
        List<PluginDescriptor> plugins = [];
        foreach (var manifestPath in Directory.EnumerateFiles(PluginsDirectory, "plugin.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath), JsonOptions)
                               ?? throw new InvalidOperationException("Plugin manifest is empty.");
                var state = stateStore.GetOrCreate(manifest.Id);
                var status = ValidateManifest(manifest, Path.GetDirectoryName(manifestPath)!, out var error);
                if (!state.IsEnabled && status == PluginStatus.Discovered)
                    status = PluginStatus.Disabled;

                if (state.RequiresRestart)
                    status = PluginStatus.PendingRestart;

                plugins.Add(new PluginDescriptor
                {
                    Manifest = manifest,
                    DirectoryPath = Path.GetDirectoryName(manifestPath)!,
                    IsEnabled = state.IsEnabled,
                    RequiresRestart = state.RequiresRestart,
                    Status = status,
                    ErrorMessage = error
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "读取插件清单失败：文件={ManifestPath}", manifestPath);
            }
        }

        return plugins;
    }

    private static void ConfigurePlugin(
        IServiceCollection services,
        PluginDescriptor descriptor,
        PluginStateStore stateStore,
        ILogger logger)
    {
        try
        {
            var plugin = CreatePlugin(descriptor);
            if (!string.Equals(plugin.Id, descriptor.Id, StringComparison.Ordinal))
                throw new InvalidOperationException("Plugin entry id does not match manifest id.");

            var pluginInfo = CreatePluginInfo(descriptor);
            Directory.CreateDirectory(pluginInfo.ConfigDirectory);

            var serviceCollection = new PluginServiceCollection(services, descriptor.Manifest);
            var buildContext = new PluginBuildContext(descriptor.Manifest, pluginInfo);
            plugin.ConfigureServices(serviceCollection, buildContext);

            services.AddSingleton(plugin);
            services.AddSingleton(provider =>
            {
                var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
                var pluginLogger = loggerFactory.CreateLogger(GetPluginLogCategoryPrefix(descriptor.Id) + "Runtime");
                var drawInvoker = new PluginDrawInvoker(
                    descriptor.Id,
                    pluginLogger,
                    provider.GetRequiredService<LinkageDrawCoordinator>(),
                    provider.GetRequiredService<IDrawTemporaryRecordService>(),
                    provider.GetRequiredService<MainConfigHandler>(),
                    provider.GetRequiredService<DrawEngine>(),
                    provider.GetRequiredService<IProfileService>(),
                    provider.GetRequiredService<IFeatureAvailabilityService>());
                var dataDirectory = pluginInfo.ConfigDirectory;
                var viewService = provider.GetService<IViewEngine>() is { } viewEngine
                    ? new PluginViewService(descriptor.Id, viewEngine)
                    : null;
                var runtimeContext = new PluginRuntimeContext(
                    descriptor.Manifest,
                    pluginInfo,
                    pluginLogger,
                    drawInvoker,
                    dataDirectory,
                    viewService);
                return new LoadedPluginRegistration(plugin, runtimeContext);
            });

            StartupResults[descriptor.Id] = (PluginStatus.Loaded, null);
            logger.LogInformation("插件已配置：插件={PluginId}，版本={Version}", descriptor.Id, descriptor.Version);
        }
        catch (Exception ex)
        {
            StartupResults[descriptor.Id] = (PluginStatus.LoadFailed, ex.Message);
            logger.LogError(ex, "插件配置失败：插件={PluginId}", descriptor.Id);
        }
    }

    private static ISecRandomPlugin CreatePlugin(PluginDescriptor descriptor)
    {
        var assemblyPath = Path.Combine(descriptor.DirectoryPath, descriptor.Manifest.EntryAssembly);
        var assembly = Assembly.LoadFrom(assemblyPath);
        var type = assembly.GetType(descriptor.Manifest.EntryType, true)
                   ?? throw new InvalidOperationException("Plugin entry type was not found.");
        return Activator.CreateInstance(type) as ISecRandomPlugin
               ?? throw new InvalidOperationException("Plugin entry type does not implement ISecRandomPlugin.");
    }

    private static PluginInfo CreatePluginInfo(PluginDescriptor descriptor)
    {
        return new PluginInfo
        {
            Manifest = descriptor.Manifest,
            PluginDirectory = descriptor.DirectoryPath,
            ConfigDirectory = Utils.GetDirectoryPath("configs", "plugins", descriptor.Id)
        };
    }

    private static string ResolveExtractedPluginDirectory(string extractDirectory)
    {
        var rootManifest = Path.Combine(extractDirectory, "plugin.json");
        if (File.Exists(rootManifest))
            return extractDirectory;

        var manifests = Directory.EnumerateFiles(extractDirectory, "plugin.json", SearchOption.AllDirectories)
            .Where(path => IsPathUnderDirectory(extractDirectory, path))
            .ToList();

        if (manifests.Count != 1)
            throw new PluginImportException(PluginImportFailureReason.InvalidManifest, "Plugin package must contain exactly one plugin.json.");

        return Path.GetDirectoryName(manifests[0])!;
    }

    private static bool IsPathUnderDirectory(string directory, string path)
    {
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginStatus ValidateManifest(PluginManifest manifest, string directoryPath, out string? error, bool allowMissingAssembly = false)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            error = "插件 ID 不能为空。";
            return PluginStatus.LoadFailed;
        }

        if (manifest.ApiVersion != SupportedApiVersion)
        {
            error = $"插件 API 版本不兼容：{manifest.ApiVersion}。";
            return PluginStatus.Incompatible;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MinimumHostVersion) &&
            Version.TryParse(manifest.MinimumHostVersion, out var minimumHostVersion) &&
            Version.TryParse(GlobalConstants.Version, out var hostVersion) &&
            hostVersion < minimumHostVersion)
        {
            error = $"插件需要 SecRandom {manifest.MinimumHostVersion} 或更高版本。";
            return PluginStatus.Incompatible;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            error = "插件入口程序集不能为空。";
            return PluginStatus.LoadFailed;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            error = "插件入口类型不能为空。";
            return PluginStatus.LoadFailed;
        }

        if (!allowMissingAssembly && !File.Exists(Path.Combine(directoryPath, manifest.EntryAssembly)))
        {
            error = "插件入口程序集不存在。";
            return PluginStatus.LoadFailed;
        }

        return PluginStatus.Discovered;
    }

    private static void DirectoryCopy(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destination = Path.Combine(targetDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static List<string> ReadTailLines(string path, int maxLines)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        Queue<string> lines = new(maxLines);
        while (reader.ReadLine() is { } line)
        {
            if (lines.Count == maxLines)
                lines.Dequeue();

            lines.Enqueue(line);
        }

        return lines.ToList();
    }

    private static IEnumerable<PluginLogEntry> ParseLogEntries(IReadOnlyList<string> lines)
    {
        PluginLogEntryBuilder? current = null;
        foreach (var line in lines)
        {
            var parts = line.Split('|', 4);
            if (parts.Length == 4 && DateTime.TryParse(parts[0], out var time))
            {
                if (current != null)
                    yield return current.Build();

                current = new PluginLogEntryBuilder(time, parts[1], parts[2], parts[3]);
                continue;
            }

            current?.AppendLine(line);
        }

        if (current != null)
            yield return current.Build();
    }

    private sealed class PluginLogEntryBuilder(DateTime time, string level, string category, string message)
    {
        private readonly StringBuilder _message = new(message);

        public void AppendLine(string line)
        {
            _message.AppendLine();
            _message.Append(line);
        }

        public PluginLogEntry Build()
        {
            return new PluginLogEntry(time, level, category, _message.ToString());
        }
    }
}
