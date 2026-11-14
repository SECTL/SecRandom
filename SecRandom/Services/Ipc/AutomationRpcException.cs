namespace SecRandom.Services.Ipc;

/// <summary>
///     v2 自动化命令的结构化失败：由 <see cref="AutomationCommandRegistry" />
///     抛出，<see cref="AutomationIpcServer" /> 负责折叠成 <c>ok:false</c> 的响应。
/// </summary>
public sealed class AutomationRpcException(
    string code,
    string message,
    bool retryable = false,
    object? details = null) : Exception(message)
{
    public string Code { get; } = code;

    public bool Retryable { get; } = retryable;

    /// <summary>可选的附加信息；只放字段名/范围/状态，不放姓名等隐私数据。</summary>
    public object? Details { get; } = details;
}