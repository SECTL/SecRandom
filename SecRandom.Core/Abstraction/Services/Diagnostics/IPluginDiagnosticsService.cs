namespace SecRandom.Core.Abstraction.Services.Diagnostics;

/// <summary>诊断条目级别。</summary>
public enum DiagnosticLevel
{
    /// <summary>普通记录。</summary>
    Info = 0,

    /// <summary>警告。</summary>
    Warning = 1,

    /// <summary>错误。</summary>
    Error = 2
}

/// <summary>一条插件诊断记录。</summary>
public sealed record PluginDiagnosticEntry(
    string PluginId,
    DateTime TimestampUtc,
    DiagnosticLevel Level,
    string Area,
    string Message);

/// <summary>宿主视角里的插件运行状态。</summary>
public sealed record PluginRuntimeInfo(
    string Id,
    string Name,
    string Version,
    string ApiVersion,
    string Status,
    string? ExceptionMessage,
    string? Directory,
    bool IsEnabled);

/// <summary>
///     插件诊断：插件把自己的运行情况报给宿主（宿主可以显示在插件详情里），
///     也可以查"我现在是什么状态、别人为什么没加载"。
/// </summary>
public interface IPluginDiagnosticsService
{
    /// <summary>上报一条诊断。</summary>
    void Report(string pluginId, string area, string message, DiagnosticLevel level = DiagnosticLevel.Info);

    /// <summary>取最近的诊断（<paramref name="pluginId" /> 为 null 表示全部）。</summary>
    IReadOnlyList<PluginDiagnosticEntry> GetEntries(string? pluginId = null, int maximumCount = 200);

    /// <summary>取宿主当前已知的全部插件状态。</summary>
    IReadOnlyList<PluginRuntimeInfo> GetLoadedPlugins();

    /// <summary>诊断或插件状态变化（可用来刷新诊断界面）。</summary>
    event EventHandler? Changed;
}
