using System.Threading;
using System.Threading.Tasks;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums.Configs;
using SecRandom.Services.Draw;
using SecRandom.Services.Linkage;

namespace SecRandom.Services.Plugins;

/// <summary>
///     Controlled plugin draw facade. Every plugin draw first passes the host course-linkage coordinator
///     and security authorization, then reuses the shared <see cref="RollCallDrawService"/> /
///     <see cref="LotteryDrawService"/> verification and transactional commit pipeline, so plugin draws
///     keep proofs, temporary-record filtering, and course restrictions identical to built-in draws.
/// </summary>
public sealed class PluginDrawService(
    RollCallDrawService rollCallDrawService,
    LotteryDrawService lotteryDrawService,
    LinkageDrawCoordinator linkageDrawCoordinator) : IPluginDrawService
{
    public async Task<PluginStudentDrawResult> DrawStudentsAsync(
        PluginStudentDrawRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorized = await linkageDrawCoordinator.AuthorizeAsync(
            SecurityOperation.RollCallStart,
            () => Task.CompletedTask,
            cancellationToken).ConfigureAwait(false);
        if (!authorized)
            return new PluginStudentDrawResult(false, [], Guid.Empty, string.Empty);

        var result = await rollCallDrawService.DrawAsync(new RollCallDrawRequest(
            request.ListName,
            request.Group,
            request.Gender,
            request.Count,
            request.CourseName), cancellationToken).ConfigureAwait(false);
        if (result is null)
            return new PluginStudentDrawResult(false, [], Guid.Empty, string.Empty);

        return new PluginStudentDrawResult(true, result.Students, result.ProofId, result.DrawRoundId);
    }

    public async Task<PluginLotteryDrawResult> DrawLotteryAsync(
        PluginLotteryDrawRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorized = await linkageDrawCoordinator.AuthorizeAsync(
            SecurityOperation.LotteryStart,
            () => Task.CompletedTask,
            cancellationToken).ConfigureAwait(false);
        if (!authorized)
            return new PluginLotteryDrawResult(false, [], [], Guid.Empty, string.Empty);

        var result = await lotteryDrawService.DrawAsync(new LotteryDrawRequest(
            request.PrizePoolName,
            request.StudentListName,
            request.Group,
            request.Gender,
            request.Count,
            request.CourseName), cancellationToken).ConfigureAwait(false);
        if (result is null)
            return new PluginLotteryDrawResult(false, [], [], Guid.Empty, string.Empty);

        return new PluginLotteryDrawResult(true, result.Prizes, result.AssignedStudents, result.PrizeProofId, result.DrawRoundId);
    }
}
