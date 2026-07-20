using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Host-internal catalog management for student lists and prize pools:
/// enumeration, creation, deletion (optionally with history files), snapshot loading,
/// and import-style full replacement. Every mutation persists immediately and re-syncs
/// the active profile when it points at the affected list.
/// </summary>
public interface IProfileCatalogManager
{
    IReadOnlyList<string> GetStudentListNames();
    IReadOnlyList<string> GetPrizeListNames();

    bool StudentListExists(string name);
    bool PrizeListExists(string name);

    bool CreateStudentList(string name);
    bool CreatePrizeList(string name);

    bool DeleteStudentList(string name, bool deleteHistory);
    bool DeletePrizeList(string name, bool deleteHistory);

    /// <summary>
    /// Loads a detached, normalized snapshot (RecordId ensured, list ordering applied and
    /// persisted when normalization changed anything). Returns null when the list is missing
    /// or unreadable; never switches the active profile.
    /// </summary>
    StudentList? LoadStudentList(string name);
    PrizeList? LoadPrizeList(string name);

    /// <summary>
    /// Persists the whole snapshot (including attached settings), normalizing RecordIds and
    /// list ordering first, then re-syncs the active profile when it uses this list.
    /// </summary>
    bool SaveStudentList(StudentList list);
    bool SavePrizeList(PrizeList list);

    /// <summary>
    /// Import boundary: replaces all records of the named list (creating it when missing),
    /// preserving attached settings. RecordIds are ensured, ordering applied, the result is
    /// saved, and the active profile is re-synced.
    /// </summary>
    bool ReplaceStudents(string name, IReadOnlyList<Student> students);
    bool ReplacePrizes(string name, IReadOnlyList<Prize> prizes);

    void SetDefaultStudentList(string name);
    void SetDefaultPrizePool(string name);

    /// <summary>
    /// Clears the named profile history in place. When the name matches the active profile,
    /// the clear is routed through the active profile service so in-memory state stays
    /// consistent. Returns false when the history file is missing; read/write failures throw.
    /// </summary>
    bool ClearStudentHistory(string name);
    bool ClearPrizeHistory(string name);
}
