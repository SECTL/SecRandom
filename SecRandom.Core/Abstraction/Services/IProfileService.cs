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

    public void RecordStudentHistory(
        IReadOnlyList<Student> students,
        DateTime now,
        int requestedCount,
        string drawGroup = "",
        string drawGender = "",
        int drawMethod = 0,
        IReadOnlyDictionary<Student, double>? weights = null);

    public void RecordPrizeHistory(IReadOnlyList<Prize> prizes, DateTime now, int requestedCount);

    public void ClearCurrentStudentHistory();
    public void ClearCurrentPrizeHistory();

    public void SaveProfile();
}
