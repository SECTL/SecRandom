using SecRandom.Core.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

public interface IProfileService
{
    public StudentList? CurrentStudentList { get; }
    public StudentHistory? CurrentStudentHistory { get; }
    
    public PrizeList? CurrentPrizeList { get; }
    public PrizeHistory? CurrentPrizeHistory { get; }

    public void SaveProfile();
}