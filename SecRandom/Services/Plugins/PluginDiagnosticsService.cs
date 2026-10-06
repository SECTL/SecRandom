using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Diagnostics;

namespace SecRandom.Services.Plugins;

/// <summary>
///     桌面实现：内存里保留最近若干条插件诊断，并把宿主自己知道的插件状态汇总出去。
///     <para>
///         数据源是 <see cref="PluginManager.Plugins" />（宿主是唯一知道"谁加载失败、为什么"的地方），
///         插件自己上报的记录走 <see cref="Report" />。生命周期钩子里的异常由
///         <c>PluginLifecycleBridge</c> 转成一条 Error 记录。
///     </para>
/// </summary>
public sealed class PluginDiagnosticsService : IPluginDiagnosticsService
{
    /// <summary>内存里最多保留多少条（超出丢最旧的）。</summary>
    private const int MaximumRetainedEntries = 500;

    private readonly ConcurrentQueue<PluginDiagnosticEntry> _entries = new();
    private readonly PluginManager _pluginManager;
    private readonly ILogger<PluginDiagnosticsService>? _logger;

    public PluginDiagnosticsService(PluginManager pluginManager, ILogger<PluginDiagnosticsService>? logger = null)
    {
        _pluginManager = pluginManager ?? throw new ArgumentNullException(nameof(pluginManager));
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Report(string pluginId, string area, string message, DiagnosticLevel level = DiagnosticLevel.Info)
    {
        var entry = new PluginDiagnosticEntry(
            string.IsNullOrWhiteSpace(pluginId) ? "unknown" : pluginId.Trim(),
            DateTime.UtcNow,
            level,
            string.IsNullOrWhiteSpace(area) ? "general" : area.Trim(),
            message ?? string.Empty);

        _entries.Enqueue(entry);
        while (_entries.Count > MaximumRetainedEntries && _entries.TryDequeue(out _))
        {
            // 只保留最近的一批，诊断不该无限增长。
        }

        switch (level)
        {
            case DiagnosticLevel.Error:
                _logger?.LogError("插件 {PluginId} 在 {Area} 上报错误：{Message}", entry.PluginId, entry.Area, entry.Message);
                break;
            case DiagnosticLevel.Warning:
                _logger?.LogWarning("插件 {PluginId} 在 {Area} 上报警告：{Message}", entry.PluginId, entry.Area, entry.Message);
                break;
            default:
                _logger?.LogDebug("插件 {PluginId} 在 {Area} 上报：{Message}", entry.PluginId, entry.Area, entry.Message);
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginDiagnosticEntry> GetEntries(string? pluginId = null, int maximumCount = 200)
    {
        var filter = string.IsNullOrWhiteSpace(pluginId) ? null : pluginId.Trim();
        var limit = Math.Clamp(maximumCount, 1, MaximumRetainedEntries);

        return _entries
            .Where(entry => filter is null || string.Equals(entry.PluginId, filter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.TimestampUtc)
            .Take(limit)
            .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginRuntimeInfo> GetLoadedPlugins() =>
        _pluginManager.Plugins
            .Select(static plugin => new PluginRuntimeInfo(
                plugin.Manifest.Id,
                plugin.Manifest.Name,
                plugin.Manifest.Version,
                plugin.Manifest.ApiVersion,
                plugin.LoadStatus.ToString(),
                plugin.Exception?.Message,
                plugin.PluginFolderPath,
                plugin.IsEnabled))
            .ToArray();
}
