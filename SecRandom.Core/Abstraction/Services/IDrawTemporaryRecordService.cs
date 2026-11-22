using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IDrawTemporaryRecordService
{
    IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string gender, string group);
    void RecordStudents(string listName, string gender, string group, IEnumerable<Student> students);
    IReadOnlyDictionary<string, int> GetGroupCounts(string listName) => new Dictionary<string, int>();
    void RecordGroups(string listName, IEnumerable<string> groups) => throw new NotSupportedException();
    void ClearGroups(string listName) => throw new NotSupportedException();
    void ClearStudentScope(string listName, string gender, string group);
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
