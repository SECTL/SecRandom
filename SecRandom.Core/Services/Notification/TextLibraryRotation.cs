using SecRandom.Core.Models.Notification;

namespace SecRandom.Core.Services.Notification;

/// <summary>
///     文案库的"平均线轮换"算法：纯计算、无 IO，可在任何线程上调用，也便于单测。
/// </summary>
/// <remarks>
///     <para>
///         规则来自产品需求"使用次数高于平均线的这一轮不用"。平均线用 <see cref="double" />，
///         不取整：30 条里只用掉 1 条时平均线约 0.033，其余 29 条仍然可用；取整会把 0 也排除，
///         直接锁死整库。
///     </para>
///     <para>
///         这里**只做选择，不改计数**：计数（<c>UseCount</c>/<c>LastUsedUtc</c>/<c>LastPickedId</c>）
///         由 <see cref="TextLibraryService.PickTemplate" /> 在真正落定后一次性写入，避免
///         "算法被调用两次就多记一次"。
///     </para>
/// </remarks>
public static class TextLibraryRotation
{
    /// <summary>
    ///     启用条目的平均使用次数；没有启用条目时返回 0。
    /// </summary>
    public static double AverageUseCount(IEnumerable<TextLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var total = 0L;
        var count = 0;
        foreach (var entry in entries)
        {
            if (!entry.Enabled)
                continue;

            total += entry.UseCount;
            count++;
        }

        return count == 0 ? 0 : (double)total / count;
    }

    /// <summary>
    ///     本轮可用的候选：启用且使用次数不高于平均线。
    /// </summary>
    public static IReadOnlyList<TextLibraryEntry> GetEligible(
        IEnumerable<TextLibraryEntry> entries,
        double average)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var eligible = new List<TextLibraryEntry>();
        foreach (var entry in entries)
        {
            if (entry.Enabled && entry.UseCount <= average)
                eligible.Add(entry);
        }

        return eligible;
    }

    /// <summary>
    ///     从候选里选一条；候选为空返回 <see langword="null" />。
    /// </summary>
    /// <param name="eligible">已经过平均线过滤的候选。</param>
    /// <param name="lastPickedId">上一次选中的条目 id，用于避免连续两次同一条。</param>
    /// <param name="nextIndex">下标生成器，入参是上限（不含），生产环境传 <c>RandomNumberGenerator.GetInt32</c>。</param>
    /// <remarks>
    ///     只在候选多于一条时剔除上次那条：只剩一条时"连续重复"是唯一可行结果，不能返回空。
    ///     下标函数越界（注入实现写错）时退回第一条，轮换是展示路径，不能把抽取带崩。
    /// </remarks>
    public static TextLibraryEntry? Pick(
        IReadOnlyList<TextLibraryEntry> eligible,
        string? lastPickedId,
        Func<int, int> nextIndex)
    {
        ArgumentNullException.ThrowIfNull(eligible);
        ArgumentNullException.ThrowIfNull(nextIndex);

        if (eligible.Count == 0)
            return null;
        if (eligible.Count == 1)
            return eligible[0];

        IReadOnlyList<TextLibraryEntry> candidates = eligible;
        if (!string.IsNullOrEmpty(lastPickedId))
        {
            var remaining = new List<TextLibraryEntry>(eligible.Count - 1);
            foreach (var entry in eligible)
            {
                if (!string.Equals(entry.Id, lastPickedId, StringComparison.Ordinal))
                    remaining.Add(entry);
            }

            // 上次那条可能已经被停用或已不在候选里，此时 remaining 与 eligible 内容一致（数量少 0）
            if (remaining.Count > 0)
                candidates = remaining;
        }

        var index = nextIndex(candidates.Count);
        if (index < 0 || index >= candidates.Count)
            index = 0;

        return candidates[index];
    }
}
