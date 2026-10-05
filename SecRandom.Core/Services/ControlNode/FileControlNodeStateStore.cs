using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Shared;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     集控节点本地状态的落盘实现：<c>data/config/control/node-state.json</c>。
/// </summary>
/// <remarks>
///     <para>
///         刻意**与 <c>settings.json</c> 分开**。这里装着本机开关与期望状态的应用进度：
///         设置备份/导入能改 settings，而"这台机器是否允许被远控"不该被一次导入悄悄打开；
///         <c>applied_desired_state_revision</c> 也不该随导出的设置回到旧值。
///     </para>
///     <para>
///         <c>node_id</c> 是**审计主体与连接身份**，因此安装时生成一次随机 128 位十六进制值并长期保持：
///         稳定（重装/重启不变）、不可猜测（不用 <c>1</c>/<c>pc-01</c> 这类可枚举值）、不含隐私
///         （不用学生姓名或教室全称，审计日志会长期保留它）。
///     </para>
///     <para>
///         写入沿用仓库的原子替换约定：先写临时文件再 <see cref="File.Move(string,string,bool)" />，
///         避免进程中途被杀留下截断 JSON。
///     </para>
/// </remarks>
public sealed class FileControlNodeStateStore : IControlNodeStateStore
{
    private const int FormatVersion = 1;

    private readonly ILogger<FileControlNodeStateStore> _logger;
    private readonly object _gate = new();
    private ControlNodeState _current;

    public FileControlNodeStateStore(ILogger<FileControlNodeStateStore> logger)
    {
        _logger = logger;
        _current = LoadOrCreate();
    }

    public ControlNodeState Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event EventHandler<ControlNodeState>? Changed;

    public void Update(Func<ControlNodeState, ControlNodeState> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);
        ControlNodeState updated;

        lock (_gate)
        {
            updated = mutate(_current);
            if (updated == _current)
                return;

            _current = updated;
        }

        Save(updated);
        Changed?.Invoke(this, updated);
    }

    private static string ResolveStatePath() =>
        Utils.GetFilePath("config", "control", "node-state.json");

    private ControlNodeState LoadOrCreate()
    {
        var path = ResolveStatePath();
        ControlNodeStateFile? stored = null;

        if (File.Exists(path))
        {
            try
            {
                stored = JsonSerializer.Deserialize<ControlNodeStateFile>(File.ReadAllText(path), ConfigServiceBase.JsonOptions);
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                // 损坏的状态文件不阻止启动：身份会重新生成，期望状态 revision 归零后由服务端重新补投。
                _logger.LogWarning(exception, "集控节点状态文件无法读取，将重新生成：{Path}", path);
            }
        }

        var state = new ControlNodeState
        {
            NodeId = stored?.NodeId?.Trim() is { Length: > 0 } nodeId ? nodeId : NewNodeId(),
            GroupId = stored?.GroupId?.Trim() ?? string.Empty,
            ServerUrl = stored?.ServerUrl?.Trim() is { Length: > 0 } serverUrl ? serverUrl : ControlNodeClientOptions.DefaultEndpoint,
            // 名字保留用户输入的原样（只把"全是空白"统一成"没填"），Trim 交给上报时的解析。
            DisplayName = string.IsNullOrWhiteSpace(stored?.DisplayName) ? null : stored!.DisplayName,
            RemoteControlEnabled = stored?.RemoteControlEnabled ?? false,
            // 默认关闭：装了新版本不会凭空多出一个"远程抽取"入口，要用户自己在集控页打开。
            RemoteDrawPageEnabled = stored?.RemoteDrawPageEnabled ?? false,
            AppliedDesiredStateRevision = stored?.AppliedDesiredStateRevision ?? ControlDesiredState.NeverSetRevision,
            DrawLocked = stored?.DrawLocked ?? false
        };

        // 首次运行（或身份丢失）时立刻落盘：node_id 必须在任何一次 hello 之前稳定下来。
        if (stored is null || !string.Equals(stored.NodeId, state.NodeId, StringComparison.Ordinal))
            Save(state);

        return state;
    }

    private static string NewNodeId() => Guid.NewGuid().ToString("n");

    private void Save(ControlNodeState state)
    {
        try
        {
            var path = ResolveStatePath();
            var payload = new ControlNodeStateFile
            {
                FormatVersion = FormatVersion,
                NodeId = state.NodeId,
                GroupId = string.IsNullOrWhiteSpace(state.GroupId) ? null : state.GroupId,
                ServerUrl = state.ServerUrl,
                DisplayName = string.IsNullOrWhiteSpace(state.DisplayName) ? null : state.DisplayName,
                RemoteControlEnabled = state.RemoteControlEnabled,
                RemoteDrawPageEnabled = state.RemoteDrawPageEnabled,
                AppliedDesiredStateRevision = state.AppliedDesiredStateRevision,
                DrawLocked = state.DrawLocked,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(payload, ConfigServiceBase.JsonOptions));
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // 落盘失败不能变成崩溃：本机开关在内存里仍然生效（保守方向是"拒绝远控"由服务端控制，
            // 但开关状态丢失只会让下次启动回到默认的关闭状态，不会放开控制）。
            _logger.LogWarning(exception, "集控节点状态保存失败。");
        }
    }

    /// <summary>持久化的状态文件形状。字段名一旦发布即不再改名。</summary>
    private sealed class ControlNodeStateFile
    {
        public int FormatVersion { get; set; } = 1;

        public string? NodeId { get; set; }

        public string? GroupId { get; set; }

        public string? ServerUrl { get; set; }

        /// <summary>控制台显示名。空/缺省表示用户没填，上报时回落到主机名。</summary>
        public string? DisplayName { get; set; }

        public bool RemoteControlEnabled { get; set; }

        /// <summary>主界面是否显示"远程抽取"页。缺省（含旧文件）即关闭。</summary>
        public bool RemoteDrawPageEnabled { get; set; }

        public long AppliedDesiredStateRevision { get; set; }

        public bool DrawLocked { get; set; }

        public DateTimeOffset? UpdatedAt { get; set; }
    }
}
