using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
/// Reads persisted profile history without changing the active profile.
/// </summary>
public interface IHistoryQueryService
{
    IReadOnlyList<string> GetStudentHistoryNames();
    IReadOnlyList<string> GetPrizeHistoryNames();
    IReadOnlyList<HistoryQueryItem> GetRecentItems(int maximumCount);

    /// <summary>
    /// Loads a full history snapshot (per-record totals, weights, items) for one profile
    /// without switching the active profile. Returns null when the profile history is
    /// missing or unreadable.
    /// </summary>
    StudentHistory? LoadStudentHistory(string name);
    PrizeHistory? LoadPrizeHistory(string name);
}

public sealed record HistoryQueryItem(
    string ProfileName,
    string RecordId,
    string DisplayName,
    DateTime DrawTime,
    string DrawRoundId,
    bool IsPrize);
