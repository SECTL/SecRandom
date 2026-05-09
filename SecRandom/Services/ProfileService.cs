using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services;

public class ProfileService : IProfileService
{
    public ProfileService()
    {
        StudentListConfig = new StudentListConfig("testing");
        StudentHistoryConfig = new StudentHistoryConfig("testing");

        PrizeListConfig = new PrizeListConfig("testing");
        PrizeHistoryConfig = new PrizeHistoryConfig("testing");

        CurrentStudentList = StudentListConfig.Data;
        CurrentStudentHistory = StudentHistoryConfig.Data;

        CurrentPrizeList = PrizeListConfig.Data;
        CurrentPrizeHistory = PrizeHistoryConfig.Data;
    }

    public StudentList? CurrentStudentList { get; set; }
    public StudentHistory? CurrentStudentHistory { get; set; }

    public PrizeList? CurrentPrizeList { get; set; }
    public PrizeHistory? CurrentPrizeHistory { get; set; }

    public StudentListConfig? StudentListConfig { get; set; }
    public StudentHistoryConfig? StudentHistoryConfig { get; set; }

    public PrizeListConfig? PrizeListConfig { get; set; }
    public PrizeHistoryConfig? PrizeHistoryConfig { get; set; }

    public void SaveProfile()
    {
        StudentListConfig?.Save();
        StudentHistoryConfig?.Save();

        PrizeListConfig?.Save();
        PrizeHistoryConfig?.Save();
    }
}