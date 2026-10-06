using Avalonia.Controls;
using Avalonia.Styling;

namespace SecRandom.Core.Abstraction.Services.Views;

/// <summary>
///     Well known host UI slots. Plugins should reference these constants instead of literal strings: a slot
///     that a shell does not render simply receives no content.
/// </summary>
public static class HostUiSlots
{
    /// <summary>Trailing area of the shell title bar (next to the app name).</summary>
    public const string MainTitleBar = "main.titlebar";

    /// <summary>Extra content beside the roll call result area.</summary>
    public const string RollCallResultExtra = "main.rollcall.result.extra";

    /// <summary>Extra content beside the lottery result area.</summary>
    public const string LotteryResultExtra = "main.lottery.result.extra";

    /// <summary>Extra content beside the quick draw result area.</summary>
    public const string QuickDrawResultExtra = "main.quickdraw.result.extra";

    /// <summary>Every slot the desktop shell can render.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        MainTitleBar,
        RollCallResultExtra,
        LotteryResultExtra,
        QuickDrawResultExtra
    ];
}

/// <summary>
///     Convenience base for <see cref="IUiContentContribution" /> implementations that only need to build a
///     visual.
/// </summary>
public abstract class UiContentContributionBase : IUiContentContribution
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string SlotId { get; }

    /// <inheritdoc />
    public virtual UiSlotKind Kind => UiSlotKind.Append;

    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public virtual bool IsEnabled => true;

    /// <inheritdoc />
    public abstract Control? CreateContent(UiSlotContext context);
}

/// <summary>
///     Convenience base for <see cref="IUiStyleContribution" /> implementations. Both collections default to
///     empty so a contribution only has to fill in what it actually uses.
/// </summary>
public abstract class UiStyleContributionBase : IUiStyleContribution
{
    private static readonly IReadOnlyDictionary<string, object?> NoResources = new Dictionary<string, object?>();
    private static readonly IReadOnlyList<IStyle> NoStyles = [];

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public virtual IReadOnlyDictionary<string, object?> Resources => NoResources;

    /// <inheritdoc />
    public virtual IReadOnlyList<IStyle> Styles => NoStyles;

    /// <inheritdoc />
    public virtual bool AppliesTo(ThemeVariant variant) => true;
}
