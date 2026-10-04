using System.Text.Json;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Shared.Models.ControlNode;

namespace SecRandom.Services.ControlNode;

/// <summary>
///     <c>settings.read</c>：把本机设置按分类读给控制台。
/// </summary>
/// <remarks>
///     <para>
///         目录本身（有哪些设置、什么类型、范围多少、能不能远程写）由
///         <see cref="ControlSettingsCatalog" /> 反射得出，这里只负责取快照与过滤。
///     </para>
///     <para>
///         读设置**不受"正在抽取"限制**：看配置是只读动作，课堂进行中恰恰是最需要看的时候。
///         但仍在 UI 线程取快照——配置对象是界面绑定的，读一半被人改了会给出自相矛盾的值。
///     </para>
/// </remarks>
public sealed class ControlSettingsReadHandler(
    MainConfigHandler config,
    ILogger<ControlSettingsReadHandler> logger)
{
    public async Task<ControlCommandOutcome> ExecuteAsync(
        JsonElement? payload,
        CancellationToken cancellationToken)
    {
        var filter = ReadCategoryFilter(payload);
        if (filter is { Count: 0 })
        {
            return ControlCommandOutcome.Failure(
                "invalid_command",
                new { hint = "categories 存在时不能为空；不传表示全部" });
        }

        var locale = ReadLocale(payload);

        try
        {
            var categories = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var all = ControlSettingsCatalog.Describe(config.Data, locale);

                return filter is null
                    ? all
                    : [.. all.Where(category => filter.Contains(category.Id))];
            });

            logger.LogInformation(
                "集控已读取设置：{Categories} 个分类（过滤：{Filter}，语言：{Locale}）",
                categories.Count, filter is null ? "无" : string.Join(",", filter), locale ?? "设备语言");

            var response = new { categories };

            // 预算自查。**必须在返回之前量**：超限的帧在传输层被丢掉，那里除了丢做不了别的，
            // 控制台只会看到一条永远不会到达结果的查询——而"读设置读不到"是无法从日志看出来的。
            // 明确的失败码让控制台能改成一次读一类，把这条路走通。
            var bytes = ControlProtocolJson.MeasureBytes(response);
            if (bytes > ControlProtocolJson.PayloadBudgetBytes)
            {
                logger.LogWarning(
                    "集控读取设置的回执超过载荷预算（{Bytes} > {Budget} 字节），已拒绝而不是发一个超限帧。",
                    bytes, ControlProtocolJson.PayloadBudgetBytes);

                return ControlCommandOutcome.Failure("payload_too_large", new
                {
                    limit = ControlProtocolJson.MaxFrameBytes,
                    budget = ControlProtocolJson.PayloadBudgetBytes,
                    bytes,
                    categories = filter?.ToArray(),
                    hint = "categories"
                });
            }

            // 过滤后一个都没命中时也要回成功 + 空列表：控制台据此知道"是筛选条件没命中"，
            // 而不是"读失败了"。
            return ControlCommandOutcome.SuccessWith(response);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "集控读取设置失败。");
            return ControlCommandOutcome.Failure(ControlRejectReasons.ExecutionFailed);
        }
    }

    /// <summary>读取可选的分类过滤；<c>null</c> 表示不过滤。</summary>
    private static HashSet<string>? ReadCategoryFilter(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return null;

        if (!root.TryGetProperty("categories", out var element) || element.ValueKind != JsonValueKind.Array)
            return null;

        var filter = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                filter.Add((item.GetString() ?? string.Empty).Trim());
        }

        return filter;
    }

    /// <summary>
    ///     读取控制台请求的界面语言（可选）。
    /// </summary>
    /// <remarks>
    ///     不在这里校验成三语之一：归一到发布语言是 <see cref="ControlSettingsLabels.NormalizePublishedLocale" />
    ///     的事，认不出来的标签**退回设备语言**而不是拒掉整次读取——控制台装的语言包与设备不必一致，
    ///     因为一个区域子标签让人读不到设置，是把协议的洁癖放在了可用性前面。
    /// </remarks>
    private static string? ReadLocale(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } root)
            return null;

        if (!root.TryGetProperty("locale", out var element) || element.ValueKind != JsonValueKind.String)
            return null;

        var locale = (element.GetString() ?? string.Empty).Trim();
        return locale.Length == 0 ? null : locale;
    }
}
