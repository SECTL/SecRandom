using System.Collections.Generic;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Abstraction.Services;

/// <summary>
///     Manages the history record groups of the loaded profile for one history kind: the "already-registered" list
///     persisted in that history file plus the group each individual record carries
///     (<see cref="SecRandom.Shared.Models.Profile.HistoryItem.HistoryGroup" />).
/// </summary>
/// <remarks>
///     <para>
///         <b>One kind per call.</b> <see cref="HistoryGroupKind.RollCall" /> reads and writes only the loaded
///         <see cref="SecRandom.Shared.Models.Profile.StudentHistory" />; <see cref="HistoryGroupKind.Lottery" /> only
///         the loaded <see cref="SecRandom.Shared.Models.Profile.PrizeHistory" />. The two files are independent, so a
///         group created for one kind is invisible to the other — see <see cref="HistoryGroupKind" /> for why. A
///         mutation persists only the touched side.
///     </para>
///     <para>
///         <b>Two sources of truth, on purpose.</b> The registered list is bookkeeping (a group can exist before it has
///         any record — "create now, draw later"), while the record's own field is the fact. They can disagree: history
///         written before this feature existed has no group at all, and a downgraded older build drops the field while
///         keeping the records. <see cref="GetGroupNames" /> therefore answers with the <b>union</b> of both
///         (registered keys plus every non-blank value that appears on a record of that kind), so a group whose
///         registration was lost still shows up and stays renameable/deletable. That union is what makes a migration
///         script unnecessary.
///     </para>
///     <para>
///         <b>No identity layer.</b> The name is the identity
///         (<see cref="SecRandom.Core.Services.Profiles.HistoryGroupName" />), so rename is an O(records) rewrite. That is
///         acceptable at profile scale (thousands of records) and keeps exports, history pages and diagnostics readable
///         without a lookup table.
///     </para>
///     <para>
///         <b>Persistence.</b> Every mutation goes through the existing per-file profile save
///         (<see cref="IProfileService.StudentHistoryConfig" /> / <see cref="IProfileService.PrizeHistoryConfig" /> →
///         the atomic temp-file-and-replace write in <c>FileConfigService</c>); this type never writes history files
///         itself. All comparisons are <see cref="StringComparison.Ordinal" />.
///     </para>
///     <para>
///         <b>Errors</b> are the stable codes on <see cref="SecRandom.Core.Services.Profiles.HistoryGroupName" />
///         (<c>group_name_*</c>, <c>group_already_exists</c>, <c>group_not_found</c>, <c>group_name_unchanged</c>,
///         <c>group_profile_unavailable</c>, <c>group_save_failed</c>), never localized prose: the app layer owns every
///         user-visible string.
///     </para>
/// </remarks>
public interface IHistoryGroupCatalog
{
    /// <summary>Registered and/or in-use group names of one history kind, excluding the default group, ordered by <see cref="StringComparer.Ordinal" />.</summary>
    IReadOnlyList<string> GetGroupNames(HistoryGroupKind kind);

    /// <summary>Registers a new (possibly still empty) group in that kind's history file.</summary>
    bool TryCreate(HistoryGroupKind kind, string name, out string error);

    /// <summary>Renames a group: rewrites the group field of every record in it, then re-registers the new name.</summary>
    bool TryRename(HistoryGroupKind kind, string from, string to, out string error);

    /// <summary>Unregisters a group and moves its records back to the default group; records are never deleted.</summary>
    void Delete(HistoryGroupKind kind, string name);

    /// <summary>Stamps <see cref="SecRandom.Shared.Models.Profile.HistoryGroupInfo.LastUsedAt" />, registering an unknown but in-use name.</summary>
    void Touch(HistoryGroupKind kind, string name);

    /// <summary>Record count of a group of that kind, derived on demand from the records themselves (never stored).</summary>
    int CountRecords(HistoryGroupKind kind, string name);
}
