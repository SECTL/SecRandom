using System.Text.Json;
using System.Text.Json.Serialization;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Shared.Models.ControlNode;

/// <summary>
///     集控载荷的序列化设置与帧预算。
/// </summary>
/// <remarks>
///     <para>
///         v4 的信封与载荷在 <see cref="V4.ControlV4Frame" /> / <see cref="V4.ControlV4Json" /> 里；
///         这里保留的是所有载荷类（名单、设置目录、语音选项……）共用的序列化口径与 64 KiB 预算，
///         它们与协议版本无关。
///     </para>
///     <para>
///         读取类能力必须**自己**按 <see cref="PayloadBudgetBytes" /> 收缩载荷，而不是等到发送时才炸：
///         发送失败发生在传输层，那里除了把帧丢掉做不了别的，控制台只会干等到命令过期。
///     </para>
/// </remarks>
public static class ControlProtocolJson
{
    /// <summary>单帧的 UTF-8 字节上限。**协议如此**：超了服务端直接断连，不会有错误帧。</summary>
    public const int MaxFrameBytes = ControlV4Protocol.MaxFrameBytes;

    /// <summary>查询载荷的预算：留出信封与转义的余量。</summary>
    public const int PayloadBudgetBytes = ControlV4Protocol.PayloadBudgetBytes;

    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>一段载荷序列化后的 UTF-8 字节数——预算检查用它，口径与传输层一致。</summary>
    public static int MeasureBytes(object payload) =>
        JsonSerializer.SerializeToUtf8Bytes(payload, Options).Length;
}