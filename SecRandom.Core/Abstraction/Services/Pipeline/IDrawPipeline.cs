using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services.Pipeline;

/// <summary>一次抽签的上下文，过滤器与后处理器都拿它判断"这次该不该我管"。</summary>
public sealed record DrawPipelineContext(
    DrawPresentationChannel Channel,
    string ListName,
    string PrizeListName,
    string GroupScope,
    string GenderScope,
    string CourseName,
    int RequestedCount,
    int DrawMethod);

/// <summary>本次参与抽签的候选集。</summary>
public sealed record DrawCandidateSet(IReadOnlyList<Student> Students, IReadOnlyList<Prize> Prizes);

/// <summary>
///     抽签前的候选集过滤/排序：可以剔除学生、调整顺序、按规则重排，甚至替换候选集。
///     <para>实现必须是纯函数式的：不要修改传入的对象，返回新的集合。按 <see cref="Priority" /> 从小到大依次执行。</para>
/// </summary>
public interface IDrawCandidateFilter
{
    /// <summary>过滤器 id（建议 <c>插件id.用途</c>）。</summary>
    string Id { get; }

    /// <summary>数值小的先执行。</summary>
    int Priority { get; }

    /// <summary>这次抽签是否需要本过滤器介入。</summary>
    bool AppliesTo(DrawPipelineContext context);

    /// <summary>过滤/排序。返回 null 表示放弃干预。</summary>
    DrawCandidateSet? Filter(DrawPipelineContext context, DrawCandidateSet candidates);
}

/// <summary>
///     抽签结果定型后的修正：改写结果、附加备注、抑制历史落库或结果展示。
///     <para>按 <see cref="Priority" /> 从小到大依次执行，后一个拿到的是前一个的输出。</para>
/// </summary>
public interface IDrawResultPostProcessor
{
    /// <summary>后处理器 id（建议 <c>插件id.用途</c>）。</summary>
    string Id { get; }

    /// <summary>数值小的先执行。</summary>
    int Priority { get; }

    /// <summary>这次抽签是否需要本处理器介入。</summary>
    bool AppliesTo(DrawPipelineContext context);

    /// <summary>修正结果。返回 null 表示不修改。</summary>
    DrawResultEdit? Process(DrawPipelineContext context, DrawResultEdit result);
}

/// <summary>结果修正的载荷。</summary>
public sealed record DrawResultEdit(
    IReadOnlyList<Student> Students,
    IReadOnlyList<Prize> Prizes,
    string? Note = null,
    bool SuppressHistory = false,
    bool SuppressPresentation = false,
    string? OverrideRoundId = null);

/// <summary>
///     宿主执行抽签流水线的入口（宿主内部用；插件通过实现过滤器/处理器参与，不需要自己调这个接口）。
/// </summary>
public interface IDrawPipelineService
{
    /// <summary>依次执行全部候选集过滤器。任一过滤器抛异常都会被跳过并记日志。</summary>
    DrawCandidateSet ApplyFilters(DrawPipelineContext context, IReadOnlyList<Student> students, IReadOnlyList<Prize> prizes);

    /// <summary>依次执行全部结果后处理器。</summary>
    DrawResultEdit ApplyPostProcessors(DrawPipelineContext context, DrawResultEdit result);

    /// <summary>当前注册的过滤器数量（诊断用）。</summary>
    int FilterCount { get; }

    /// <summary>当前注册的后处理器数量（诊断用）。</summary>
    int PostProcessorCount { get; }
}
