using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services;

public class ProfileService : IProfileService
{
    public StudentList? CurrentStudentList { get; set; }
    public StudentHistory? CurrentStudentHistory { get; set; }
    
    public PrizeList? CurrentPrizeList { get; set; }
    public PrizeHistory? CurrentPrizeHistory { get; set; }

    private StudentListConfig? _studentListConfig = null;
    private StudentHistoryConfig? _studentHistoryConfig = null;
    
    private PrizeListConfig? _prizeListConfig = null;
    private PrizeHistoryConfig? _prizeHistoryConfig = null;

    public ProfileService()
    {
        _studentListConfig = new StudentListConfig("testing");
        _studentHistoryConfig = new StudentHistoryConfig("testing");
        
        _prizeListConfig = new PrizeListConfig("testing");
        _prizeHistoryConfig = new PrizeHistoryConfig("testing");

        CurrentStudentList = _studentListConfig.Data;
        CurrentStudentHistory = _studentHistoryConfig.Data;
        
        CurrentPrizeList = _prizeListConfig.Data;
        CurrentPrizeHistory = _prizeHistoryConfig.Data;
    }
    
    public void SaveProfile()
    {
        _studentListConfig?.Save();
        _studentHistoryConfig?.Save();
        
        _prizeListConfig?.Save();
        _prizeHistoryConfig?.Save();
    }
}