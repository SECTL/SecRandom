using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Draw;

public partial class DrawEngine
{
    /// <summary>
    ///     Per-student history figures for the scheduler: how often each candidate was drawn and when it was last
    ///     drawn, read through exactly the same course/history-group scope as the fairness weights.
    /// </summary>
    /// <remarks>
    ///     The orchestration scheduler needs the raw counts (not only the resulting weights) because a candidate
    ///     whose weight is zero is not excluded there: the round still has to seat it, and the counts are what order
    ///     that deferred tail (most-drawn first, then longest-unseen).
    /// </remarks>
    public IReadOnlyDictionary<Guid, (int Count, DateTime LastDrawn)> GetStudentHistoryFigures(
        IReadOnlyList<Student> candidates,
        string courseName = "",
        string? historyGroup = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var pool = candidates.Where(candidate => candidate.IsCandidate).ToList();
        if (pool.Count == 0)
            return new Dictionary<Guid, (int Count, DateTime LastDrawn)>();

        var cache = BuildStudentHistoryCache(pool, courseName, historyGroup);
        var figures = new Dictionary<Guid, (int Count, DateTime LastDrawn)>(cache.Count);
        foreach (var pair in cache)
        {
            if (pair.Key.RecordId == Guid.Empty)
                ProfileRecordIdentity.EnsureRecordId(pair.Key);
            figures[pair.Key.RecordId] = (pair.Value.TotalCount, pair.Value.LastDrawnTime);
        }
        return figures;
    }
}