using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     History snapshot compensation used by <see cref="DrawCommitCoordinator"/>. Implemented by the Core
///     profile service; snapshots are opaque JSON captured before mutation, so restoring one also removes any
///     history items written for the failed round.
/// </summary>
internal interface IDrawHistorySnapshotCompensation
{
    string CaptureStudentHistorySnapshot();
    void RestoreStudentHistorySnapshot(string snapshotJson);
    string CapturePrizeHistorySnapshot();
    void RestorePrizeHistorySnapshot(string snapshotJson);
}

/// <summary>
///     Temporary-record scope compensation used by <see cref="DrawCommitCoordinator"/>. A null snapshot means
///     the scope did not exist before the commit and is removed on rollback.
/// </summary>
internal interface IDrawTemporaryRecordCompensation
{
    string? CaptureStudentScopeSnapshot(string listName, string gender, string group);
    void RestoreStudentScopeSnapshot(string listName, string gender, string group, string? snapshotJson);
    string? CapturePrizeScopeSnapshot(string listName);
    void RestorePrizeScopeSnapshot(string listName, string? snapshotJson);
}

/// <summary>
///     Single transactional commit boundary for all draw channels. Order: temporary records first, then
///     persistent history, all under one DrawRoundId. Any failure restores the pre-commit snapshots so a partial
///     commit cannot desync repeat limits (temporary records) from fairness weights (persistent history).
///     TODO(Worker_DrawCommit): proof attestation ordering (commit before attestation dispatch) is deferred to
///     stage 2; see the draw-stack recon report section 7 item 10.
/// </summary>
internal sealed class DrawCommitCoordinator(
    ILogger<DrawCommitCoordinator> logger,
    IProfileService profileService,
    IDrawTemporaryRecordService temporaryRecordService) : IDrawCommitService
{
    private readonly object _gate = new();

    public string CommitStudentDraw(StudentDrawCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var roundId = ResolveRoundId(commit.DrawRoundId);
        if (commit.Winners.Count == 0)
            return roundId;

        lock (_gate)
        {
            var historyCompensation = profileService as IDrawHistorySnapshotCompensation;
            var recordCompensation = temporaryRecordService as IDrawTemporaryRecordCompensation;
            var historySnapshot = historyCompensation?.CaptureStudentHistorySnapshot();
            var recordSnapshot = recordCompensation?.CaptureStudentScopeSnapshot(commit.ListName, commit.GenderScope, commit.GroupScope);

            try
            {
                temporaryRecordService.RecordStudents(commit.ListName, commit.GenderScope, commit.GroupScope, commit.Winners);
                profileService.RecordStudentHistory(
                    commit.Winners,
                    commit.DrawTime,
                    commit.RequestedCount,
                    commit.GroupScope,
                    commit.GenderScope,
                    commit.DrawMethod,
                    commit.Weights,
                    commit.CourseName,
                    roundId);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "学生抽取提交失败，执行补偿回滚：DrawRoundId={DrawRoundId}。", roundId);
                if (historyCompensation is not null && historySnapshot is not null)
                    TryCompensate(() => historyCompensation.RestoreStudentHistorySnapshot(historySnapshot), roundId);
                if (recordCompensation is not null)
                    TryCompensate(() => recordCompensation.RestoreStudentScopeSnapshot(commit.ListName, commit.GenderScope, commit.GroupScope, recordSnapshot), roundId);
                throw;
            }

            return roundId;
        }
    }

    public string CommitLotteryDraw(LotteryDrawCommit commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        var roundId = ResolveRoundId(commit.DrawRoundId);
        if (commit.Prizes.Count == 0)
            return roundId;

        lock (_gate)
        {
            var hasAssignedStudents = commit.AssignedStudents is { Count: > 0 };
            var studentListName = commit.StudentListName ?? string.Empty;
            var historyCompensation = profileService as IDrawHistorySnapshotCompensation;
            var recordCompensation = temporaryRecordService as IDrawTemporaryRecordCompensation;
            var prizeHistorySnapshot = historyCompensation?.CapturePrizeHistorySnapshot();
            var prizeRecordSnapshot = recordCompensation?.CapturePrizeScopeSnapshot(commit.PrizeListName);
            var studentHistorySnapshot = hasAssignedStudents ? historyCompensation?.CaptureStudentHistorySnapshot() : null;
            var studentRecordSnapshot = hasAssignedStudents
                ? recordCompensation?.CaptureStudentScopeSnapshot(studentListName, commit.StudentGenderScope, commit.StudentGroupScope)
                : null;

            try
            {
                temporaryRecordService.RecordPrizes(commit.PrizeListName, commit.Prizes);
                profileService.RecordPrizeHistory(commit.Prizes, commit.DrawTime, commit.RequestedCount, commit.PrizeDrawMethod, roundId);
                if (hasAssignedStudents)
                {
                    temporaryRecordService.RecordStudents(studentListName, commit.StudentGenderScope, commit.StudentGroupScope, commit.AssignedStudents!);
                    profileService.RecordStudentHistory(
                        commit.AssignedStudents!,
                        commit.DrawTime,
                        commit.AssignedStudents!.Count,
                        commit.StudentGroupScope,
                        commit.StudentGenderScope,
                        commit.StudentDrawMethod,
                        weights: null,
                        commit.CourseName,
                        roundId);
                }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "抽奖提交失败，执行补偿回滚：DrawRoundId={DrawRoundId}。", roundId);
                if (hasAssignedStudents)
                {
                    if (historyCompensation is not null && studentHistorySnapshot is not null)
                        TryCompensate(() => historyCompensation.RestoreStudentHistorySnapshot(studentHistorySnapshot), roundId);
                    if (recordCompensation is not null)
                        TryCompensate(() => recordCompensation.RestoreStudentScopeSnapshot(studentListName, commit.StudentGenderScope, commit.StudentGroupScope, studentRecordSnapshot), roundId);
                }

                if (historyCompensation is not null && prizeHistorySnapshot is not null)
                    TryCompensate(() => historyCompensation.RestorePrizeHistorySnapshot(prizeHistorySnapshot), roundId);
                if (recordCompensation is not null)
                    TryCompensate(() => recordCompensation.RestorePrizeScopeSnapshot(commit.PrizeListName, prizeRecordSnapshot), roundId);
                throw;
            }

            return roundId;
        }
    }

    private static string ResolveRoundId(string? drawRoundId) =>
        string.IsNullOrWhiteSpace(drawRoundId) ? Guid.NewGuid().ToString("N") : drawRoundId;

    private void TryCompensate(Action compensate, string roundId)
    {
        try
        {
            compensate();
        }
        catch (Exception compensationException)
        {
            logger.LogError(compensationException, "抽取提交补偿回滚失败：DrawRoundId={DrawRoundId}。", roundId);
        }
    }
}
