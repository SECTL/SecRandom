using System;
using System.Collections.Generic;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IProfileService
{
    public StudentList? CurrentStudentList { get; }
    public StudentHistory? CurrentStudentHistory { get; }

    public PrizeList? CurrentPrizeList { get; }
    public PrizeHistory? CurrentPrizeHistory { get; }

    public StudentListConfig? StudentListConfig { get; }
    public StudentHistoryConfig? StudentHistoryConfig { get; }

    public PrizeListConfig? PrizeListConfig { get; }
    public PrizeHistoryConfig? PrizeHistoryConfig { get; }

    public void LoadStudentProfile(string name, bool saveCurrent = true);
    public void LoadPrizeProfile(string name, bool saveCurrent = true);

    /// <param name="historyGroup">
    ///     History record group the written records belong to; empty (the default) keeps today's global-history
    ///     behaviour, so existing callers are unaffected.
    /// </param>
    public void RecordStudentHistory(
        IReadOnlyList<Student> students,
        DateTime now,
        int requestedCount,
        string drawGroup = "",
        string drawGender = "",
        int drawMethod = 0,
        IReadOnlyDictionary<Student, double>? weights = null,
        string courseName = "",
        string? drawRoundId = null,
        string historyGroup = "");

    /// <param name="courseName">
    ///     Linkage course the prize records belong to; empty keeps global prize history. Prize history had no
    ///     course dimension before history groups, so this parameter is additive for the same reason.
    /// </param>
    /// <param name="historyGroup">History record group; empty is the default group.</param>
    public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount, int drawMethod = 0, string? drawRoundId = null, string courseName = "", string historyGroup = "");

    public void ClearCurrentStudentHistory();
    public void ClearCurrentPrizeHistory();

    public void SaveProfile();
}
