namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Reads persisted profile history without changing the active profile.
/// </summary>
public interface IHistoryQueryService
{
    IReadOnlyList<string> GetStudentHistoryNames();
    IReadOnlyList<string> GetPrizeHistoryNames();
    IReadOnlyList<HistoryQueryItem> GetRecentItems(int maximumCount);
}

public sealed record HistoryQueryItem(
    string ProfileName,
    string RecordId,
    string DisplayName,
    DateTime DrawTime,
    string DrawRoundId,
    bool IsPrize);
