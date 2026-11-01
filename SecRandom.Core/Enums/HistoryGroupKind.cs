namespace SecRandom.Core.Enums;

/// <summary>
///     Which of a profile's two history files a history-group operation applies to.
/// </summary>
/// <remarks>
///     <para>
///         History groups are scoped per history kind rather than shared across both: roll-call and lottery history are
///         two separate files (<c>data/history/roll_call_history</c>, <c>data/history/lottery_history</c>) with two
///         separate settings pages and two separate main-page selectors, and the candidate pools behind them are
///         unrelated. A shared name would surface a group that only exists in the other file — an empty group in the
///         roll-call picker whose records all live in the lottery history — which is exactly the place a "this group has
///         no records yet" hint is least likely to be read correctly.
///     </para>
///     <para>
///         Declared in <c>SecRandom.Core.Enums</c> rather than <c>SecRandom.Core.Enums.Configs</c> on purpose: the
///         <c>Configs</c> subtree holds values that are persisted in <c>settings.json</c>, while this is a
///         persistence-scope discriminator passed to a service call and never stored. Values are explicit because the
///         app layer may map them to resource keys in a switch.
///     </para>
/// </remarks>
public enum HistoryGroupKind
{
    /// <summary>Roll-call (student) history: <see cref="SecRandom.Shared.Models.Profile.StudentHistory" />.</summary>
    RollCall = 0,

    /// <summary>Lottery (prize) history: <see cref="SecRandom.Shared.Models.Profile.PrizeHistory" />.</summary>
    Lottery = 1
}
