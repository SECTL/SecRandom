using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Temporary ("this round") draw records: the repeat-limit counters, never persistent history.
/// </summary>
/// <remarks>
///     Student counters are keyed by the <c>historyGroup</c> scope the draw ran with, so "已抽过 N 次" is counted
///     inside one record group: <c>null</c> means the draw was not scoped to a group at all (the history-group
///     feature is off or the page selector is hidden) and uses the single all-groups bucket, <c>""</c> means the
///     default record group, and any other value is that group's name. Prize counters deliberately stay one bucket
///     per prize pool, because they carry the physical inventory (<see cref="Prize.Count" />) and splitting them per
///     group would let the same prizes be counted as in stock once per group.
/// </remarks>
public interface IDrawTemporaryRecordService
{
    /// <summary>Reads one student bucket; <paramref name="historyGroup" /> is the draw's group scope.</summary>
    IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string? historyGroup);
    /// <summary>
    /// Sums one student list's counters across every bucket, i.e. the progress a whole-list reset clears.
    /// </summary>
    IReadOnlyDictionary<string, int> GetStudentCountsForList(string listName);
    void RecordStudents(string listName, string? historyGroup, IEnumerable<Student> students);
    /// <summary>
    /// Clears one student bucket (the current record group's counters) while the other groups keep their progress;
    /// used by both the manual reset and the per-round automation reset.
    /// </summary>
    void ClearStudentScope(string listName, string? historyGroup);
    /// <summary>
    /// Moves a student bucket to another group name, merging the counts when the target already has one. Used when a
    /// record group is renamed, so the round does not silently restart for the renamed group.
    /// </summary>
    void RenameStudentScope(string listName, string from, string to);
    /// <summary>
    /// Replaces the complete student temporary-record file with an empty state
    /// without deleting the file
    /// </summary>
    void ResetStudentList(string listName);
    void ClearStudentList(string listName);
    void ClearStudentListOnce(string listName);
    IReadOnlyDictionary<string, int> GetPrizeCounts(string listName);
    void RecordPrizes(string listName, IEnumerable<Prize> prizes);
    /// <summary>
    /// Replaces the complete prize temporary-record file with an empty state
    /// without deleting the file
    /// </summary>
    void ResetPrizeList(string listName);
    void ClearPrizeList(string listName);
    void ClearPrizeListOnce(string listName);
    void ClearAll();
}
