using SecRandom.Core.Abstraction.Services;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Profiles;

/// <summary>
///     Non-mutating profile snapshots use the same roster catalog/database readers as profile management;
///     history snapshots continue through the dedicated history query service.
/// </summary>
public sealed class ProfileQueryService(IProfileCatalogManager catalog, IHistoryQueryService historyQuery) : IProfileQueryService
{
    public StudentList? LoadStudentList(string name) => catalog.LoadStudentList(name);
    public PrizeList? LoadPrizeList(string name) => catalog.LoadPrizeList(name);
    public StudentHistory? LoadStudentHistory(string name) => historyQuery.LoadStudentHistory(name);
    public PrizeHistory? LoadPrizeHistory(string name) => historyQuery.LoadPrizeHistory(name);

}
