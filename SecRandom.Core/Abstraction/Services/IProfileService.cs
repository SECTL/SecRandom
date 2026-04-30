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
    
    public void SaveProfile();
}