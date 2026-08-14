namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Controlled draw facade for plugins. Plugins must never call <c>IDrawCommitService</c> directly;
///     every plugin draw goes through the host's course-linkage coordinator, security authorization, and
///     the shared verification/commit pipeline so draws stay fair, reproducible, and course-restricted.
/// </summary>
public interface IPluginDrawService
{
    /// <summary>Draws students through the host point-call pipeline. Returns a failed result when not authorized.</summary>
    Task<PluginStudentDrawResult> DrawStudentsAsync(
        PluginStudentDrawRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Draws prizes (and optionally assigns students) through the host lottery pipeline.</summary>
    Task<PluginLotteryDrawResult> DrawLotteryAsync(
        PluginLotteryDrawRequest request,
        CancellationToken cancellationToken = default);
}
