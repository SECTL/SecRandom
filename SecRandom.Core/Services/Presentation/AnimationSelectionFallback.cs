using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;
using Microsoft.Extensions.Logging;

namespace SecRandom.Core.Services.Presentation;

/// <summary>
///     "动画样式"里选中的插件动画一旦随插件被卸载/禁用而消失，配置里存的那个 id 就成了死值：
///     下拉框解析不到它（列表会空着），<see cref="IResultPresentationService" /> 也不会让任何
///     呈现器接管，宿主自己的动画同样不播——整场抽签就没了演出。
///     这里在启动时按"贡献项是否还在"把这种死值退回内置动画（<see cref="DrawAnimationSelection.Host" />），
///     用户之前选的内置样式（<see cref="DrawSettingsConfigBase.AnimationStyle" />）保持不变。
/// </summary>
public static class AnimationSelectionFallback
{
    /// <summary>
    ///     把四份抽取设置里指向已消失插件动画的选择退回内置动画。
    /// </summary>
    /// <param name="config">宿主主配置；为 null 时什么都不做。</param>
    /// <param name="contributions">当前宿主里实际存在的动画贡献项；为 null 时按"一个都没有"处理。</param>
    /// <param name="logger">可选日志。</param>
    /// <returns>被退回默认值的设置项数量。</returns>
    public static int Apply(MainConfigModel? config,
        IEnumerable<IDrawAnimationContribution>? contributions,
        ILogger? logger = null)
    {
        if (config is null)
            return 0;

        var available = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (contributions is not null)
        {
            foreach (var contribution in contributions)
            {
                if (!string.IsNullOrWhiteSpace(contribution.Id))
                    available.Add(contribution.Id.Trim());
            }
        }

        var reset = 0;
        foreach (var (section, settings) in Enumerate(config))
        {
            // 空值按旧配置的"没选过"处理：宿主会当内置动画用，不动它。
            var selectedId = settings.PluginAnimationId;
            if (string.IsNullOrWhiteSpace(selectedId))
                continue;

            if (string.Equals(selectedId, DrawAnimationSelection.Host, StringComparison.OrdinalIgnoreCase))
                continue;

            if (available.Contains(selectedId.Trim()))
                continue;

            settings.PluginAnimationId = DrawAnimationSelection.Host;
            reset++;

            logger?.LogInformation(
                "{Section} 里选中的插件动画 {AnimationId} 已经不存在了（插件被卸载或禁用），动画样式已恢复为宿主内置动画。",
                section, selectedId);
        }

        return reset;
    }

    private static IEnumerable<(string Section, DrawSettingsConfigBase Settings)> Enumerate(MainConfigModel config)
    {
        yield return (nameof(MainConfigModel.DefaultDrawSettings), config.DefaultDrawSettings);
        yield return (nameof(MainConfigModel.RollCallSettings), config.RollCallSettings);
        yield return (nameof(MainConfigModel.QuickDrawSettings), config.QuickDrawSettings);
        yield return (nameof(MainConfigModel.LotterySettings), config.LotterySettings);
    }
}
