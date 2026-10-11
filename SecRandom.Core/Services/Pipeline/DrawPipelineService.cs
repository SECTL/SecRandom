using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services.Pipeline;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Pipeline;

/// <summary>
///     <see cref="IDrawPipelineService" /> 的默认实现：按 <c>Priority</c> 升序依次跑过滤器 / 后处理器。
///     <para>
///         单个插件出错只会跳过它自己那一步（记一条 warning），不会中断抽签。
///     </para>
/// </summary>
public sealed class DrawPipelineService : IDrawPipelineService
{
    private readonly IReadOnlyList<IDrawCandidateFilter> _filters;
    private readonly IReadOnlyList<IDrawResultPostProcessor> _postProcessors;
    private readonly ILogger<DrawPipelineService>? _logger;

    public DrawPipelineService(
        IEnumerable<IDrawCandidateFilter>? filters = null,
        IEnumerable<IDrawResultPostProcessor>? postProcessors = null,
        ILogger<DrawPipelineService>? logger = null)
    {
        _filters = (filters ?? []).OrderBy(static filter => filter.Priority).ToArray();
        _postProcessors = (postProcessors ?? []).OrderBy(static processor => processor.Priority).ToArray();
        _logger = logger;
    }

    /// <summary>当前已挂上的过滤器数量（诊断用）。</summary>
    public int FilterCount => _filters.Count;

    /// <summary>当前已挂上的后处理器数量（诊断用）。</summary>
    public int PostProcessorCount => _postProcessors.Count;

    /// <inheritdoc />
    public DrawCandidateSet ApplyFilters(
        DrawPipelineContext context,
        IReadOnlyList<Student> students,
        IReadOnlyList<Prize> prizes)
    {
        var working = new DrawCandidateSet(students ?? [], prizes ?? []);

        foreach (var filter in _filters)
        {
            try
            {
                if (!filter.AppliesTo(context))
                    continue;

                var next = filter.Filter(context, working);
                if (next is not null)
                    working = next;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "候选集过滤器 {FilterId} 处理 {Channel} 通道的抽签时出错，已按原候选集继续。",
                    filter.Id,
                    context.Channel);
            }
        }

        return working;
    }

    /// <inheritdoc />
    public DrawResultEdit ApplyPostProcessors(DrawPipelineContext context, DrawResultEdit result)
    {
        var working = result ?? new DrawResultEdit([], []);

        foreach (var processor in _postProcessors)
        {
            try
            {
                if (!processor.AppliesTo(context))
                    continue;

                var next = processor.Process(context, working);
                if (next is not null)
                    working = next;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(
                    ex,
                    "结果后处理器 {ProcessorId} 处理 {Channel} 通道的抽签时出错，已按原结果继续。",
                    processor.Id,
                    context.Channel);
            }
        }

        return working;
    }
}
