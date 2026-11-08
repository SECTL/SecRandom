using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Enums;
using SecRandom.Shared.ComponentModels;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Core.Services.Profiles;

/// <summary>
///     Default <see cref="IHistoryGroupCatalog" />: per-kind group CRUD over the loaded profile's roll-call or lottery
///     history file, persisted through that file's own config handler.
/// </summary>
/// <remarks>
///     <para>
///         Identity is the raw stored name, compared with <see cref="StringComparison.Ordinal" />; only names the caller
///         supplies for <i>creation</i> go through <see cref="HistoryGroupName.Normalize" />. Rename and delete therefore
///         accept exactly the string that <see cref="GetGroupNames" /> returned, which is what keeps imported or
///         hand-edited files (whose group values may not be trimmed the way this build writes them) actionable instead
///         of unaddressable.
///     </para>
///     <para>
///         <b>Only the touched file is written.</b> Each mutation saves through
///         <see cref="IProfileService.StudentHistoryConfig" /> or <see cref="IProfileService.PrizeHistoryConfig" /> —
///         never <see cref="IProfileService.SaveProfile" />, which would also rewrite the roster and the other history
///         file for an operation that cannot have changed them. Creating a roll-call group must not touch lottery
///         history (and vice versa), because a needless rewrite is a needless chance to lose the other file to a
///         half-finished write or to clobber an edit made between the two saves.
///     </para>
///     <para>
///         <see cref="Touch" /> deliberately does <b>not</b> save. It only stamps
///         <see cref="HistoryGroupInfo.LastUsedAt" /> in memory, so a caller that touches a group immediately before the
///         draw writes its history gets the stamp persisted by that same history save — touching must not turn every
///         draw into an extra profile write. A touch that no later save follows is lost, which is exactly the right
///         weight for advisory bookkeeping.
///     </para>
/// </remarks>
internal sealed class HistoryGroupCatalog(
    ILogger<HistoryGroupCatalog> logger,
    IProfileService profileService) : IHistoryGroupCatalog
{
    private readonly object _gate = new();

    public IReadOnlyList<string> GetGroupNames(HistoryGroupKind kind)
    {
        var view = ResolveKind(kind);
        return view is null ? [] : CollectGroupNames(view);
    }

    public bool TryCreate(HistoryGroupKind kind, string name, out string error)
    {
        if (!HistoryGroupName.Validate(name, out var normalized, out error))
            return false;

        lock (_gate)
        {
            var view = ResolveKind(kind);
            if (view is null)
            {
                error = HistoryGroupName.ErrorProfileUnavailable;
                return false;
            }

            if (ContainsName(CollectGroupNames(view), normalized))
            {
                error = HistoryGroupName.ErrorNameExists;
                return false;
            }

            view.Groups[normalized] = new HistoryGroupInfo { CreatedAt = DateTime.Now };
            return TrySave(view, "create", normalized, out error);
        }
    }

    public bool TryRename(HistoryGroupKind kind, string from, string to, out string error)
    {
        if (!HistoryGroupName.Validate(to, out var target, out error))
            return false;

        lock (_gate)
        {
            var view = ResolveKind(kind);
            if (view is null)
            {
                error = HistoryGroupName.ErrorProfileUnavailable;
                return false;
            }

            if (string.Equals(from, target, StringComparison.Ordinal))
            {
                error = HistoryGroupName.ErrorNameUnchanged;
                return false;
            }

            var existing = CollectGroupNames(view);
            if (string.IsNullOrWhiteSpace(from) || !ContainsName(existing, from))
            {
                error = HistoryGroupName.ErrorNameNotFound;
                return false;
            }

            if (ContainsName(existing, target))
            {
                error = HistoryGroupName.ErrorNameExists;
                return false;
            }

            var rewritten = RewriteRecords(view.Records, from, target);
            MoveRegistration(view.Groups, from, target);

            logger.LogInformation("已重命名历史记录组：类型={Kind}，旧名称={OldName}，新名称={NewName}，重写记录={RecordCount}。",
                kind, from, target, rewritten);

            return TrySave(view, "rename", target, out error);
        }
    }

    public void Delete(HistoryGroupKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_gate)
        {
            var view = ResolveKind(kind);
            if (view is null)
                return;

            // Records are never deleted with a group: they fall back to the implicit default group, so deleting a
            // group can never lose a draw record.
            var changed = RewriteRecords(view.Records, name, string.Empty);
            if (view.Groups.Remove(name))
                changed++;

            if (changed == 0)
                return;

            logger.LogInformation("已删除历史记录组：类型={Kind}，名称={GroupName}，影响项={ChangedCount}。",
                kind, name, changed);

            if (!TrySave(view, "delete", name, out var error))
                logger.LogWarning("历史记录组删除后保存失败：类型={Kind}，名称={GroupName}，错误={Error}。", kind, name, error);
        }
    }

    public void Touch(HistoryGroupKind kind, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        lock (_gate)
        {
            var view = ResolveKind(kind);
            if (view is null)
                return;

            var now = DateTime.Now;
            if (view.Groups.TryGetValue(name, out var info))
            {
                info.LastUsedAt = now;
                return;
            }

            // Self-healing: a record can carry a group that was never registered (history written by this build after a
            // downgrade dropped the list, or a hand-edited file). Touching it registers it instead of failing.
            if (CountRecords(view.Records, name) == 0)
                return;

            view.Groups[name] = new HistoryGroupInfo { CreatedAt = now, LastUsedAt = now };
        }
    }

    public int CountRecords(HistoryGroupKind kind, string name)
    {
        var view = ResolveKind(kind);
        return view is null ? 0 : CountRecords(view.Records, name ?? string.Empty);
    }

    /// <summary>Both live views of one history file: its group list, its records, and how that file is saved.</summary>
    private sealed record KindView(
        ObservableDictionary<string, HistoryGroupInfo> Groups,
        ObservableDictionary<string, History> Records,
        Action Save);

    private KindView? ResolveKind(HistoryGroupKind kind)
    {
        switch (kind)
        {
            case HistoryGroupKind.RollCall:
            {
                var history = profileService.CurrentStudentHistory;
                return history is null
                    ? null
                    : new KindView(history.HistoryGroups, history.Students, SaveStudentHistory);
            }

            case HistoryGroupKind.Lottery:
            {
                var history = profileService.CurrentPrizeHistory;
                return history is null
                    ? null
                    : new KindView(history.HistoryGroups, history.Prizes, SavePrizeHistory);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown history group kind.");
        }
    }

    private void SaveStudentHistory() => profileService.StudentHistoryConfig?.Save();

    private void SavePrizeHistory() => profileService.PrizeHistoryConfig?.Save();

    private static int CountRecords(ObservableDictionary<string, History> records, string target)
    {
        var count = 0;
        foreach (var record in records.Values)
            foreach (var item in record.Histories)
                if (string.Equals(item.HistoryGroup ?? string.Empty, target, StringComparison.Ordinal))
                    count++;

        return count;
    }

    private static int RewriteRecords(ObservableDictionary<string, History> records, string from, string to)
    {
        var rewritten = 0;
        foreach (var record in records.Values)
        {
            foreach (var item in record.Histories)
            {
                if (!string.Equals(item.HistoryGroup ?? string.Empty, from, StringComparison.Ordinal))
                    continue;

                item.HistoryGroup = to;
                rewritten++;
            }
        }

        return rewritten;
    }

    private static void MoveRegistration(
        ObservableDictionary<string, HistoryGroupInfo> groups,
        string from,
        string to)
    {
        var info = groups.TryGetValue(from, out var existing)
            ? existing
            : new HistoryGroupInfo { CreatedAt = DateTime.Now };

        groups.Remove(from);
        groups[to] = info;
    }

    private static List<string> CollectGroupNames(KindView view)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var key in view.Groups.Keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
                names.Add(key);
        }

        foreach (var record in view.Records.Values)
        {
            foreach (var item in record.Histories)
            {
                if (!string.IsNullOrWhiteSpace(item.HistoryGroup))
                    names.Add(item.HistoryGroup!);
            }
        }

        return [.. names];
    }

    private static bool ContainsName(List<string> names, string name) =>
        names.Any(candidate => string.Equals(candidate, name, StringComparison.Ordinal));

    private bool TrySave(KindView view, string operation, string name, out string error)
    {
        try
        {
            view.Save();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            // The in-memory change stays: the next successful save (or an explicit retry) persists it, and reverting a
            // half-applied rename across a history file would be the fragile option.
            logger.LogError(exception, "保存历史记录组失败：操作={Operation}，名称={GroupName}。", operation, name);
            error = HistoryGroupName.ErrorSaveFailed;
            return false;
        }
    }
}
