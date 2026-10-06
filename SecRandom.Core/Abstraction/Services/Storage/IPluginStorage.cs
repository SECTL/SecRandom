using System.Text.Json;

namespace SecRandom.Core.Abstraction.Services.Storage;

/// <summary>
///     插件私有存储：所有路径都相对于插件自己的配置目录，越界路径（<c>..</c>、绝对路径、盘符）一律被拒绝。
///     <para>
///         由宿主按插件 id 分配：插件在 <c>PluginBase.Initialize</c> 里通过
///         <see cref="IPluginStorageFactory" /> 取自己的那一份即可，不需要自己拼路径。
///     </para>
/// </summary>
public interface IPluginStorage
{
    /// <summary>拥有这块存储的插件 id。</summary>
    string PluginId { get; }

    /// <summary>插件配置目录的绝对路径（只读用；写文件请用本接口的方法，路径会被校验）。</summary>
    string RootDirectory { get; }

    /// <summary>把若干段相对路径拼成绝对路径，同时校验越界。</summary>
    string GetPath(params string[] relativeParts);

    /// <summary>相对路径是否存在（文件或目录）。</summary>
    bool Exists(string relativePath);

    /// <summary>列出目录下的文件名（相对路径），<paramref name="relativeDirectory" /> 为空表示根目录。</summary>
    IReadOnlyList<string> ListFiles(string relativeDirectory = "", bool recursive = false);

    /// <summary>读文本；不存在时返回 null。</summary>
    string? ReadText(string relativePath);

    /// <summary>写文本（自动建目录，UTF-8 无 BOM）。</summary>
    void WriteText(string relativePath, string content);

    /// <summary>读字节；不存在时返回 null。</summary>
    byte[]? ReadBytes(string relativePath);

    /// <summary>写字节（自动建目录）。</summary>
    void WriteBytes(string relativePath, byte[] content);

    /// <summary>读 JSON；文件不存在或内容损坏时返回 default。</summary>
    T? ReadJson<T>(string relativePath, JsonSerializerOptions? options = null);

    /// <summary>写 JSON（缩进、UTF-8 无 BOM）。</summary>
    void WriteJson<T>(string relativePath, T value, JsonSerializerOptions? options = null);

    /// <summary>删除文件或目录；返回是否真的删掉了东西。</summary>
    bool Delete(string relativePath);

    /// <summary>当前占用的字节数（用于排查"插件把磁盘塞满了"）。</summary>
    long GetUsedBytes();
}

/// <summary>按插件 id 创建 <see cref="IPluginStorage" />。</summary>
public interface IPluginStorageFactory
{
    /// <summary>取（或创建）某个插件的存储。</summary>
    IPluginStorage Create(string pluginId);

    /// <summary>插件 id 非法时返回 false，而不是抛异常。</summary>
    bool TryCreate(string pluginId, out IPluginStorage? storage);
}
