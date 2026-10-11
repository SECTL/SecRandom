using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using SecRandom.Core.Abstraction.Services.Storage;

namespace SecRandom.Core.Services.Storage;

/// <summary>
///     插件私有存储的默认实现：所有路径都被限制在插件自己的目录里，越界一律拒绝。
/// </summary>
public sealed class PluginStorage : IPluginStorage
{
    private static readonly JsonSerializerOptions DefaultJsonOptions = new(JsonSerializerDefaults.General)
    {
        WriteIndented = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly string _rootWithSeparator;

    public PluginStorage(string pluginId, string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        PluginId = pluginId;
        _root = Path.GetFullPath(rootDirectory);
        _rootWithSeparator = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public string PluginId { get; }

    /// <inheritdoc />
    public string RootDirectory => _root;

    /// <inheritdoc />
    public string GetPath(params string[] relativeParts)
    {
        var relative = CombineRelative(relativeParts);
        var full = Path.GetFullPath(Path.Combine(_root, relative));

        if (!full.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"插件 {PluginId} 的存储路径越界：'{relative}' 不在自己的目录里。");
        }

        return full;
    }

    /// <inheritdoc />
    public bool Exists(string relativePath)
    {
        var path = GetPath(relativePath);
        return File.Exists(path) || Directory.Exists(path);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ListFiles(string relativeDirectory = "", bool recursive = false)
    {
        var directory = GetPath(relativeDirectory);
        if (!Directory.Exists(directory))
            return [];

        var files = Directory.GetFiles(
            directory,
            "*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        return files
            .Select(file => Path.GetRelativePath(_root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public string? ReadText(string relativePath)
    {
        var path = GetPath(relativePath);
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }

    /// <inheritdoc />
    public void WriteText(string relativePath, string content)
    {
        var path = GetPath(relativePath);
        EnsureDirectory(path);
        File.WriteAllText(path, content ?? string.Empty, Utf8NoBom);
    }

    /// <inheritdoc />
    public byte[]? ReadBytes(string relativePath)
    {
        var path = GetPath(relativePath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <inheritdoc />
    public void WriteBytes(string relativePath, byte[] content)
    {
        var path = GetPath(relativePath);
        EnsureDirectory(path);
        File.WriteAllBytes(path, content ?? []);
    }

    /// <inheritdoc />
    public T? ReadJson<T>(string relativePath, JsonSerializerOptions? options = null)
    {
        var text = ReadText(relativePath);
        if (string.IsNullOrWhiteSpace(text))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(text, options ?? DefaultJsonOptions);
        }
        catch (JsonException)
        {
            // 文件被手改坏了：当作"没有数据"，而不是让插件崩掉。
            return default;
        }
    }

    /// <inheritdoc />
    public void WriteJson<T>(string relativePath, T value, JsonSerializerOptions? options = null)
    {
        var json = JsonSerializer.Serialize(value, options ?? DefaultJsonOptions);
        WriteText(relativePath, json);
    }

    /// <inheritdoc />
    public bool Delete(string relativePath)
    {
        var path = GetPath(relativePath);

        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }

        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public long GetUsedBytes()
    {
        if (!Directory.Exists(_root))
            return 0;

        long total = 0;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (IOException)
            {
                // 统计用，读不到就跳过。
            }
        }

        return total;
    }

    private static string CombineRelative(string[] relativeParts)
    {
        if (relativeParts is null || relativeParts.Length == 0)
            return string.Empty;

        var segments = new List<string>(relativeParts.Length);
        foreach (var part in relativeParts)
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var segment = part.Replace('\\', '/').Trim();

            if (segment.Contains("..", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException($"插件存储路径不允许包含 '..'：'{part}'。");
            }

            if (Path.IsPathRooted(segment))
            {
                throw new UnauthorizedAccessException($"插件存储只接受相对路径：'{part}'。");
            }

            segments.Add(segment.Trim('/'));
        }

        return string.Join(Path.DirectorySeparatorChar, segments);
    }

    private static void EnsureDirectory(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }
}

/// <summary>
///     按插件 id 分配存储目录（<c>&lt;root&gt;\&lt;pluginId&gt;</c>），同一个 id 复用同一个实例。
/// </summary>
public sealed class PluginStorageFactory : IPluginStorageFactory
{
    private readonly ConcurrentDictionary<string, IPluginStorage> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _rootDirectory;

    public PluginStorageFactory(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>插件存储的根目录。</summary>
    public string RootDirectory => _rootDirectory;

    /// <inheritdoc />
    public IPluginStorage Create(string pluginId)
    {
        if (!TryCreate(pluginId, out var storage))
            throw new ArgumentException($"非法的插件 id：'{pluginId}'。", nameof(pluginId));

        return storage!;
    }

    /// <inheritdoc />
    public bool TryCreate(string pluginId, out IPluginStorage? storage)
    {
        storage = null;

        if (string.IsNullOrWhiteSpace(pluginId))
            return false;

        var trimmed = pluginId.Trim();

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            return false;
        }

        storage = _cache.GetOrAdd(
            trimmed,
            static (id, root) => new PluginStorage(id, Path.Combine(root, id)),
            _rootDirectory);

        return true;
    }
}
