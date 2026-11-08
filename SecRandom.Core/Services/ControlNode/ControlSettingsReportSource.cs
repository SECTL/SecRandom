using Microsoft.Extensions.Logging;
using SecRandom.Core.Models;
using SecRandom.Shared.Models.ControlNode.V4;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     生成本次 <c>hello</c> 要上报的 <c>settings</c> 段（schema + 单调 revision）。
/// </summary>
/// <remarks>
///     会话层只依赖这个接口：真实实现读 <c>MainConfigHandler.Data</c> 并落盘 revision，
///     测试可以直接给一份固定 schema，不必构造整个配置处理器。
/// </remarks>
public interface IControlSettingsReportSource
{
    /// <summary>
    ///     构建上报内容；取不到本机配置时返回 <c>null</c>（hello 仍然照发，只是不带 settings）。
    /// </summary>
    ControlV4SettingsReport? Build();
}

/// <summary>
///     默认实现：从当前主配置导出 schema，并把 revision/内容哈希写回节点状态文件。
/// </summary>
/// <remarks>
///     <para>
///         revision 只在序列化内容变化时 +1，因此同一份 schema 重连、重启都复用同一个值；
///         服务端按设备记录最高 revision，重启后从 1 重数会被判为 stale。
///     </para>
///     <para>
///         这里**不抛异常**：schema 生成失败只意味着这次 hello 不带 settings，不能因此让节点
///         连不上。失败原因写日志，且下一次重连会再试。
///     </para>
/// </remarks>
public sealed class ControlSettingsReportSource(
    Func<MainConfigModel> configAccessor,
    IControlNodeStateStore stateStore,
    ILogger<ControlSettingsReportSource> logger)
    : IControlSettingsReportSource
{
    public ControlV4SettingsReport? Build()
    {
        try
        {
            var result = ControlSettingsSchemaBuilder.Build(configAccessor());
            var schema = result.Schema;
            var hash = ControlSettingsSchemaBuilder.ComputeHash(schema);

            var current = stateStore.Current;
            var revision = ControlSettingsSchemaBuilder.ResolveRevision(
                current.SettingsSchemaRevision, current.SettingsSchemaHash, hash);

            if (revision != current.SettingsSchemaRevision
                || !string.Equals(hash, current.SettingsSchemaHash, StringComparison.OrdinalIgnoreCase))
            {
                stateStore.Update(state => state with
                {
                    SettingsSchemaRevision = revision,
                    SettingsSchemaHash = hash
                });

                logger.LogInformation(
                    "集控设置 schema 已更新：revision {Revision}，{Count} 项。",
                    revision, schema.Count);
            }

            if (result.Issues.Count > 0)
            {
                // 丢弃项不会让 schema 失效，但必须能解释"为什么控制台少了一项"。
                logger.LogWarning(
                    "集控设置 schema 有 {Count} 项未上报：{Issues}",
                    result.Issues.Count, string.Join(", ", result.Issues));
            }

            return new ControlV4SettingsReport
            {
                Revision = revision,
                Schema = schema
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控设置 schema 生成失败，本次 hello 不携带 settings。");
            return null;
        }
    }
}