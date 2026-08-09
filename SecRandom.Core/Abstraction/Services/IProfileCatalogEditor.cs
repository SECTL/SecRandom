using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Host-internal mutation boundary for the active student list and prize pool.
/// </summary>
public interface IProfileCatalogEditor
{
    IReadOnlyList<Student> GetStudents();
    IReadOnlyList<Prize> GetPrizes();
    bool AddStudent(string name, string id);
    bool AddPrize(string name, string id);
    bool SetStudentEnabled(string recordId, bool enabled);
    bool SetPrizeEnabled(string recordId, bool enabled);
    bool RemoveStudent(string recordId);
    bool RemovePrize(string recordId);
}
