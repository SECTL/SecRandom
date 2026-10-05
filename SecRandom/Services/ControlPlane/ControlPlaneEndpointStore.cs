using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SecRandom.Shared;

namespace SecRandom.Services.ControlPlane;

/// <summary>
///     控制面（集控 REST）基址：这台设备要连哪一个集控平台。
/// </summary>
/// <remarks>
///     <para>
///         <b>刻意不进 <c>settings.json</c>。</b>原因与集控节点地址同一条：这个地址每次请求都会收到本账号的
///         SECTL access token。设置文件是可以导入、导出、随云备份搬到别的机器上的——一次导入就能把令牌
///         指向别人的服务器，那不是"改了个地址"，那是把账号交出去。所以它单独落在
///         <c>data/config/control-plane/endpoint.json</c>，与节点侧的
///         <c>data/config/control/node-state.json</c> 是同一条取舍。
///     </para>
///     <para>
///         读取一律容错：文件缺失、被截断、内容不是 JSON、地址不合法——都回落到
///         <see cref="ControlPlaneClient.DefaultBaseUrl" />。一个记不住地址的客户端不该打不开抽取页。
///     </para>
/// </remarks>
public interface IControlPlaneEndpointStore
{
    /// <summary>当前生效的基址。没改过时就是线上默认地址。</summary>
    string Current { get; }

    /// <summary>是否被改成了非默认地址（"恢复默认"按钮据此启用）。</summary>
    bool IsCustom { get; }

    /// <summary>校验并落盘。地址不合法时**什么都不写**，只回错误码。</summary>
    bool TryUpdate(string? endpoint, out string? error);

    /// <summary>丢弃自定义地址，回到线上默认。</summary>
    void ResetToDefault();

    /// <summary>基址变化（含恢复默认）。参数是新的生效地址。</summary>
    event EventHandler<string>? Changed;
}

/// <summary>
///     把控制面基址写进 <c>data/config/control-plane/endpoint.json</c>。
/// </summary>
/// <remarks>
///     落盘沿用本项目的原子替换约定（先写临时文件再覆盖）：直接覆盖时断电会留下半个文件，
///     下次启动就会退回默认地址——而这台机器本来是要连自建平台的。
/// </remarks>
public sealed class FileControlPlaneEndpointStore : IControlPlaneEndpointStore
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly ILogger<FileControlPlaneEndpointStore> _logger;
    private readonly string _filePath;
    private readonly object _gate = new();
    private string? _customEndpoint;

    public FileControlPlaneEndpointStore(
        ILogger<FileControlPlaneEndpointStore> logger,
        string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Utils.GetFilePath("config", "control-plane", "endpoint.json");
        _customEndpoint = Load();
    }

    public string Current
    {
        get
        {
            lock (_gate)
            {
                return _customEndpoint ?? ControlPlaneClient.DefaultBaseUrl;
            }
        }
    }

    public bool IsCustom
    {
        get
        {
            lock (_gate)
            {
                return _customEndpoint is not null;
            }
        }
    }

    public event EventHandler<string>? Changed;

    public bool TryUpdate(string? endpoint, out string? error)
    {
        if (!ControlPlaneEndpointPolicy.TryValidate(endpoint, out var normalized, out error))
            return false;

        // 走到这里 normalized 一定非空（策略只在通过时给出它），编译器看不出来是因为 out 是开着的类型。
        var target = normalized!;

        // 手输线上默认地址等于恢复默认：否则文件里会留下一条与默认逐字相同的"自定义地址"，
        // 界面上的"恢复默认"按钮也就永远亮着。
        if (string.Equals(target, ControlPlaneClient.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            ResetToDefault();
            return true;
        }

        var effective = Current;
        lock (_gate)
        {
            if (string.Equals(_customEndpoint, target, StringComparison.Ordinal))
                return true;

            Save(target);
            _customEndpoint = target;
            effective = target;
        }

        Changed?.Invoke(this, effective);
        return true;
    }

    public void ResetToDefault()
    {
        bool changed;
        lock (_gate)
        {
            changed = _customEndpoint is not null;
            if (!changed)
                return;

            _customEndpoint = null;
            DeleteOrRewriteAsDefault();
        }

        Changed?.Invoke(this, ControlPlaneClient.DefaultBaseUrl);
    }

    /// <summary>读文件；任何异常都当成"没配置过"。</summary>
    private string? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var stored = JsonSerializer.Deserialize<ControlPlaneEndpointFile>(
                File.ReadAllText(_filePath), JsonOptions);

            // 文件里存的是**已经规范化过**的地址，但文件可以被手改：读回来照样重新校验一次。
            return ControlPlaneEndpointPolicy.TryValidate(stored?.BaseUrl, out var normalized, out _)
                   && !string.Equals(normalized, ControlPlaneClient.DefaultBaseUrl, StringComparison.OrdinalIgnoreCase)
                ? normalized
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException
                                              or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(exception, "控制面地址文件无法读取，将使用默认地址：{Path}", _filePath);
            return null;
        }
    }

    /// <summary>删除自定义地址文件；删不掉就写一个"就是默认"的文件覆盖它。</summary>
    /// <remarks>
    ///     关键是**不能留下一个仍然指向自定义地址的文件**：那会让"恢复默认"在本次运行里生效、
    ///     下次启动又变回去。写默认值是退而求其次的稳妥方向——读回来仍然等价于"没配置过"。
    /// </remarks>
    private void DeleteOrRewriteAsDefault()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogWarning(exception, "控制面地址文件无法删除，将改写为默认地址：{Path}", _filePath);
        }

        Save(ControlPlaneClient.DefaultBaseUrl);
    }

    private void Save(string endpoint)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var payload = new ControlPlaneEndpointFile
            {
                FormatVersion = FormatVersion,
                BaseUrl = endpoint,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var temporary = _filePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(payload, JsonOptions));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 落盘失败不阻断：地址在本次运行里照常生效（界面显示的是内存值），
            // 只是重启后会回到默认——比抛异常打断用户输入好，且必须留下一条能查的日志。
            _logger.LogWarning(exception, "控制面地址保存失败：{Path}", _filePath);
        }
    }

    /// <summary>持久化的文件形状。字段名一旦发布即不再改名。</summary>
    private sealed class ControlPlaneEndpointFile
    {
        [JsonPropertyName("format_version")]
        public int FormatVersion { get; set; } = 1;

        [JsonPropertyName("base_url")]
        public string? BaseUrl { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
