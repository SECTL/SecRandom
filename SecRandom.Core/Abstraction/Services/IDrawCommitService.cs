using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     One logical student draw to persist: temporary records first, then history, all under a single DrawRoundId.
///     DrawMethod carries the channel's draw type ((int)DrawType for student channels).
/// </summary>
public sealed record StudentDrawCommit(
    IReadOnlyList<Student> Winners,
    DateTime DrawTime,
    int RequestedCount,
    string ListName,
    string GroupScope = "",
    string GenderScope = "",
    int DrawMethod = 0,
    IReadOnlyDictionary<Student, double>? Weights = null,
    string CourseName = "",
    string? DrawRoundId = null);

/// <summary>
///     One logical lottery draw. Prizes and their assigned students share a single DrawRoundId so history
///     projections (IPC/history queries) can pair them. PrizeDrawMethod carries (int)LotteryDrawType;
///     StudentDrawMethod carries (int)DrawType of the student assignment channel.
/// </summary>
public sealed record LotteryDrawCommit(
    IReadOnlyList<Prize> Prizes,
    DateTime DrawTime,
    int RequestedCount,
    string PrizeListName,
    IReadOnlyList<Student>? AssignedStudents = null,
    string? StudentListName = null,
    string StudentGroupScope = "",
    string StudentGenderScope = "",
    int PrizeDrawMethod = 0,
    int StudentDrawMethod = 0,
    string CourseName = "",
    string? DrawRoundId = null);

/// <summary>
///     Transactional commit boundary for every draw channel (pages, quick draw, mobile
///     sessions): one DrawRoundId per logical draw, temporary records before persistent history, and
///     compensating rollback (history snapshot restore + temporary-record scope restore) when any step fails.
/// </summary>
public interface IDrawCommitService
{
    /// <summary>Commits a student draw and returns the DrawRoundId shared by all written history items.</summary>
    string CommitStudentDraw(StudentDrawCommit commit);

    /// <summary>Commits a lottery draw; prize and assigned-student history items share the returned DrawRoundId.</summary>
    string CommitLotteryDraw(LotteryDrawCommit commit);
}
