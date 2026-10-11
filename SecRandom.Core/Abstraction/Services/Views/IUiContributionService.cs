using Avalonia.Controls;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>How a contribution combines with the host's own content in a slot.</summary>
public enum UiSlotKind
{
    /// <summary>Add the content after the host's own content.</summary>
    Append = 0,

    /// <summary>Add the content before the host's own content.</summary>
    Prepend = 1,

    /// <summary>Replace the host's own content with the contribution (highest priority wins).</summary>
    Replace = 2,

    /// <summary>Hide the host's own content (the contribution itself is not rendered).</summary>
    Hide = 3
}

/// <summary>Context handed to a contribution when the host asks it to build its visual.</summary>
/// <param name="SlotId">Slot being built.</param>
/// <param name="DataContext">Data context of the host control that owns the slot, when it has one.</param>
public sealed record UiSlotContext(string SlotId, object? DataContext = null)
{
    /// <summary>Extra values the host publishes for a slot (page id, channel, theme name, ...).</summary>
    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>();
}

/// <summary>
///     Plugin provided content for a named host slot. Register it during <c>PluginBase.Initialize</c>:
///     <code>services.AddSingleton&lt;IUiContentContribution, MyBannerContribution&gt;();</code>
///     The host resolves contributions from DI and re-builds slots when they change.
/// </summary>
public interface IUiContentContribution
{
    /// <summary>Stable unique id.</summary>
    string Id { get; }

    /// <summary>Slot this contribution targets, for example <c>main.rollcall.result.extra</c>.</summary>
    string SlotId { get; }

    /// <summary>How the content combines with the host's own content.</summary>
    UiSlotKind Kind => UiSlotKind.Append;

    /// <summary>Higher priority wins for <see cref="UiSlotKind.Replace" /> and orders the others.</summary>
    int Priority => 0;

    /// <summary>Turn the contribution off without unregistering it.</summary>
    bool IsEnabled => true;

    /// <summary>Build the visual for <paramref name="context" />, or <c>null</c> to contribute nothing.</summary>
    Control? CreateContent(UiSlotContext context);
}

/// <summary>
///     Host slot capability exposed to plugins. Slots are named regions of the host UI (title bar, result
///     areas, ...); a contribution may append to, prepend to, replace or hide the region's own content.
///     Resolvable through <c>IAppHost.TryGetService&lt;IUiContributionService&gt;()</c>.
/// </summary>
public interface IUiContributionService
{
    /// <summary>All registered contributions.</summary>
    IReadOnlyList<IUiContentContribution> Contributions { get; }

    /// <summary>Ids of the slots the current shell exposes.</summary>
    IReadOnlyList<string> SlotIds { get; }

    /// <summary>Raised when contributions change, so slot hosts can rebuild.</summary>
    event EventHandler? Changed;

    /// <summary>Register a contribution, replacing any existing one with the same id.</summary>
    void Add(IUiContentContribution contribution);

    /// <summary>Unregister a contribution by id.</summary>
    bool Remove(string id);

    /// <summary>Enabled contributions for one slot, highest priority first.</summary>
    IReadOnlyList<IUiContentContribution> GetSlotContributions(string slotId);

    /// <summary><c>true</c> when a <see cref="UiSlotKind.Hide" /> contribution hides the slot's own content.</summary>
    bool IsSlotHidden(string slotId);

    /// <summary>The replacement visual for a slot, when a <see cref="UiSlotKind.Replace" /> contribution wins.</summary>
    Control? BuildReplacement(string slotId, UiSlotContext context);
}
