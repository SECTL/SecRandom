using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IDrawTemporaryRecordService
{
    IReadOnlyDictionary<string, int> GetStudentCounts(string listName, string gender, string group);
    void RecordStudents(string listName, string gender, string group, IEnumerable<Student> students);
    void ClearStudentScope(string listName, string gender, string group);
    void ClearStudentList(string listName);
    void ClearAll();
}
