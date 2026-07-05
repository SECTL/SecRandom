using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IDrawTemporaryRecordService
{
    IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string gender, string group);
    void RecordStudents(string listName, string gender, string group, IEnumerable<Student> students);
    void ClearStudentScope(string listName, string gender, string group);
    void ClearStudentList(string listName);
    void ClearStudentListOnce(string listName);
    IReadOnlyDictionary<string, int> GetPrizeCounts(string listName);
    void RecordPrizes(string listName, IEnumerable<Prize> prizes);
    void ClearPrizeList(string listName);
    void ClearPrizeListOnce(string listName);
    void ClearAll();
}
