using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Profiles;

public interface IProfileQueryService
{
    StudentList? LoadStudentList(string name);
    PrizeList? LoadPrizeList(string name);
    StudentHistory? LoadStudentHistory(string name);
    PrizeHistory? LoadPrizeHistory(string name);
}
