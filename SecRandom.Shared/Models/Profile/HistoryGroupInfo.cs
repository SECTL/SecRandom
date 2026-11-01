using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models.Profile;

/// <summary>
///     Bookkeeping for one user-named history group. The group name is the identity: history items carry it in
///     <see cref="HistoryItem.HistoryGroup"/>, so this type deliberately stores no record count. Any displayed
///     number must be derived from the history items themselves, because a second source of truth would drift
///     the moment a record is moved between groups or an archive is restored.
/// </summary>
public partial class HistoryGroupInfo : ObservableRecipient
{
    [ObservableProperty] private DateTime _createdAt = DateTime.Now;
    [ObservableProperty] private DateTime _lastUsedAt = DateTime.MinValue;
}
