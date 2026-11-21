using System.Text.Json.Serialization;

namespace SecRandom.Core.Models.Orchestration;

/// <summary>
///     Which built-in draw channel an arranged list belongs to. A roll-call arrangement also serves quick draw,
///     because both channels draw from the same student roster and share its temporary records.
/// </summary>
public enum OrchestrationTarget
{
    RollCall = 0,
    Lottery = 1
}

/// <summary>
///     The five rule lists one arranged profile owns. Every list answers a different question, so the profile keeps
///     them apart instead of mixing them into one table: 排斥 = 谁不参加（全局）, 权重 = 谁的相对概率更高,
///     延后 = 谁这一轮先不抽, 顺序 = 这一轮的出场队列, 必中 = 这一轮谁必须排在最前.
/// </summary>
public enum OrchestrationRuleKind
{
    Exclude = 0,
    Weight = 1,
    Defer = 2,
    Order = 3,
    Guarantee = 4
}

/// <summary>
///     Which of the five rule lists this profile actually uses.
/// </summary>
/// <remarks>
///     「顺序」和「必中」回答的是同一个问题——这一轮谁先出场——所以两者在开关层面互斥：打开其中一个就把另一个
///     关掉。<see cref="SetEnabled" /> 是这条互斥的唯一入口，页面和任何调用方都走它，免得两个开关同时亮着而
///     运行时只能靠隐藏的优先级裁决。关掉一个规则只停止使用它的数据，不会删除名单内容。
/// </remarks>
public sealed class OrchestrationRules
{
    /// <summary>排斥名单：这些人完全不参加本名单的任何一轮。</summary>
    public bool Exclude { get; set; } = true;

    /// <summary>权重名单：本轮手动权重作为公平权重的乘数。</summary>
    public bool Weight { get; set; } = true;

    /// <summary>延后名单：这些人本轮的抽取往后放（只影响配置的那一轮）。</summary>
    public bool Defer { get; set; } = true;

    /// <summary>顺序名单：本轮按队列顺序出场。</summary>
    public bool Order { get; set; } = true;

    /// <summary>必中名单：这些人本轮排在最前。与 <see cref="Order" /> 互斥。</summary>
    public bool Guarantee { get; set; }

    public bool IsEnabled(OrchestrationRuleKind kind) => kind switch
    {
        OrchestrationRuleKind.Exclude => Exclude,
        OrchestrationRuleKind.Weight => Weight,
        OrchestrationRuleKind.Defer => Defer,
        OrchestrationRuleKind.Order => Order,
        OrchestrationRuleKind.Guarantee => Guarantee,
        _ => false
    };

    /// <summary>
    ///     Turns one rule list on or off. Enabling 顺序 turns 必中 off and the other way round, because a round can
    ///     only follow one answer to "who comes out first".
    /// </summary>
    public void SetEnabled(OrchestrationRuleKind kind, bool enabled)
    {
        switch (kind)
        {
            case OrchestrationRuleKind.Exclude:
                Exclude = enabled;
                break;
            case OrchestrationRuleKind.Weight:
                Weight = enabled;
                break;
            case OrchestrationRuleKind.Defer:
                Defer = enabled;
                break;
            case OrchestrationRuleKind.Order:
                Order = enabled;
                if (enabled)
                    Guarantee = false;
                break;
            case OrchestrationRuleKind.Guarantee:
                Guarantee = enabled;
                if (enabled)
                    Order = false;
                break;
        }
    }

    public OrchestrationRules Clone() => new()
    {
        Exclude = Exclude,
        Weight = Weight,
        Defer = Defer,
        Order = Order,
        Guarantee = Guarantee
    };
}

/// <summary>
///     One arranged draw list: a fixed schedule of who comes out, in which round, in which order.
/// </summary>
/// <remarks>
///     The profile is deliberately independent of the roster it draws from (<see cref="SourceListName" /> is a
///     name, not a copy of the list). Members are keyed by <see cref="OrchestrationMember.RecordId" /> so a
///     rename or renumber in the roster never silently re-points a scheduled position at someone else; a member
///     whose record disappeared is only flagged <see cref="OrchestrationMember.IsSourceMissing" />.
/// </remarks>
public sealed class OrchestrationProfile
{
    public const int MaxRounds = 10;

    /// <summary>2 = 五名单（排斥/权重/延后/顺序/必中）；1 是只有排除/延后/定序/权重字段的旧格式。</summary>
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public OrchestrationTarget Target { get; set; }

    public string SourceListName { get; set; } = string.Empty;

    /// <summary>Only one profile per (target, source list) is active at a time.</summary>
    public bool IsActive { get; set; }

    /// <summary>How many rounds one cycle has (1..<see cref="MaxRounds" />).</summary>
    public int RoundCount { get; set; } = 1;

    /// <summary>Which of the five rule lists this profile uses.</summary>
    public OrchestrationRules Rules { get; set; } = new();

    public List<OrchestrationMember> Members { get; set; } = [];

    public List<OrchestrationRound> Rounds { get; set; } = [];

    [JsonIgnore]
    public int EffectiveRoundCount => Math.Clamp(RoundCount, 1, MaxRounds);

    public OrchestrationMember? FindMember(Guid recordId) =>
        Members.FirstOrDefault(member => member.RecordId == recordId);

    /// <summary>
    ///     Returns the per-round arrangement for <paramref name="round" />, creating it on first use. Rounds are
    ///     stored sparsely: a round with no order, skips, guarantees, or weights has no row until something is
    ///     configured.
    /// </summary>
    public OrchestrationRound GetRound(int round)
    {
        var index = Math.Clamp(round, 1, EffectiveRoundCount);
        var existing = Rounds.FirstOrDefault(candidate => candidate.Index == index);
        if (existing is not null)
            return existing;

        var created = new OrchestrationRound { Index = index };
        Rounds.Add(created);
        Rounds.Sort((left, right) => left.Index.CompareTo(right.Index));
        return created;
    }

    /// <summary>
    ///     Sets the global exclusion of one member. Excluding clears every trace of that member in every round
    ///     (a member who never takes part must not stay in a round's queue, skip, guarantee, or weight list),
    ///     while un-excluding only lifts the flag so no arrangement is silently rewritten.
    /// </summary>
    public void SetExcluded(Guid recordId, bool excluded)
    {
        var member = FindMember(recordId);
        if (member is null)
            return;

        member.IsExcluded = excluded;
        if (excluded is false)
            return;

        foreach (var round in Rounds)
            round.RemoveMember(recordId);
    }

    /// <summary>
    ///     Postpones one member for one round. Postponing lifts a global exclusion and removes the member from that
    ///     round's queue and guarantee group, because someone who does not take part cannot also be scheduled.
    /// </summary>
    public void SetSkipped(OrchestrationRound round, Guid recordId, bool skipped)
    {
        ArgumentNullException.ThrowIfNull(round);
        var has = round.Skipped.Contains(recordId);
        if (skipped)
        {
            var member = FindMember(recordId);
            if (member is not null)
                member.IsExcluded = false;
            if (!has)
                round.Skipped.Add(recordId);
            round.Order.Remove(recordId);
            round.Guaranteed.Remove(recordId);
            return;
        }

        if (has)
            round.Skipped.Remove(recordId);
    }

    /// <summary>Adds or removes one member from the round's 必中 group. 必中 and 顺序 are mutually exclusive.</summary>
    public void SetGuaranteed(OrchestrationRound round, Guid recordId, bool guaranteed)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (guaranteed)
            SetSkipped(round, recordId, false);

        var has = round.Guaranteed.Contains(recordId);
        if (guaranteed)
        {
            if (!has)
                round.Guaranteed.Add(recordId);
            round.Order.Remove(recordId);
            return;
        }

        if (has)
            round.Guaranteed.Remove(recordId);
    }

    /// <summary>
    ///     Writes the round's 顺序 queue. The sequence is the round's lineup: members omitted from it still take
    ///     part and are filled in by weight behind it, so a partial queue is a preference rather than a filter.
    /// </summary>
    public void SetOrder(OrchestrationRound round, IReadOnlyList<Guid> sequence)
    {
        ArgumentNullException.ThrowIfNull(round);
        ArgumentNullException.ThrowIfNull(sequence);
        var members = Members.Select(member => member.RecordId).ToHashSet();
        round.Order = sequence
            .Where(members.Contains)
            .Distinct()
            .ToList();
        // 顺序与必中互斥：写队列等于说「这一轮按队列走」，必中名单的优先级不再参与。
        foreach (var recordId in round.Order)
            round.Guaranteed.Remove(recordId);
    }

    /// <summary>
    ///     Sets (or clears) one member's 权重 multiplier for one round. Weight 0 is not an exclusion: it moves the
    ///     member behind the weighted fill while everyone still takes part.
    /// </summary>
    public void SetWeight(OrchestrationRound round, Guid recordId, double? weight)
    {
        ArgumentNullException.ThrowIfNull(round);
        if (weight is null)
            round.Weights.Remove(recordId);
        else
            round.Weights[recordId] = Math.Clamp(weight.Value, 0d, 100d);
    }
}

/// <summary>One scheduled participant; the roster record itself stays in the student list / prize pool.</summary>
public sealed class OrchestrationMember
{
    public Guid RecordId { get; set; }

    /// <summary>Display snapshot (student number / prize number) so a deleted source record stays readable.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Display snapshot; never used as identity.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Global exclusion: this member never takes part in any round.</summary>
    public bool IsExcluded { get; set; }

    /// <summary>The roster record is gone; the row is kept so the schedule can be repaired by hand.</summary>
    public bool IsSourceMissing { get; set; }
}

/// <summary>
///     Per-round arrangement: the round's 延后 / 顺序 / 必中 / 权重 lists. Every collection is "what differs from
///     the default", so an untouched round costs nothing: no queue means the weighted fill decides, no skip means
///     everyone takes part, no guarantee means nobody jumps the queue, no weight means the fair weight alone
///     decides.
/// </summary>
public sealed class OrchestrationRound
{
    public int Index { get; set; }

    /// <summary>
    ///     The round's 顺序 queue, in draw order. It is a queue, not a set of absolute positions: a member that
    ///     already drew (or was postponed / excluded) leaves the queue, and everyone behind moves up one place.
    ///     That is what makes 「第 3 个抽中」 actually mean the third draw of the round instead of the third slot of
    ///     the current remainder.
    /// </summary>
    /// <remarks>
    ///     Members missing from the queue still take part; the weighted fill seats them behind it, so a partial
    ///     queue is a preference, not a filter.
    /// </remarks>
    public List<Guid> Order { get; set; } = [];

    /// <summary>Record ids that sit this round out (延后); they return automatically in the next round.</summary>
    public List<Guid> Skipped { get; set; } = [];

    /// <summary>
    ///     Record ids that must come out first this round (必中). The group is ordered by the effective weight, so
    ///     a weight still decides who of the guaranteed five goes first, and then by the order they were added.
    ///     Mutually exclusive with <see cref="Order" /> at the switch level.
    /// </summary>
    public List<Guid> Guaranteed { get; set; } = [];

    /// <summary>Manual multipliers on the fair weight, keyed by record id. Missing means 1.</summary>
    public Dictionary<Guid, double> Weights { get; set; } = [];

    /// <summary>
    ///     Format-1 「第几个抽中」 pins, kept only so an existing file can be migrated: the store folds them into
    ///     <see cref="Order" /> on read and always writes this property as <c>null</c>. A pin was an absolute slot
    ///     in the shrinking remainder, which is exactly the bug the queue replaced.
    /// </summary>
    [JsonPropertyName("Pins")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OrchestrationPin>? LegacyPins { get; set; }

    /// <summary>Drops every arrangement this round carries for one member.</summary>
    public void RemoveMember(Guid recordId)
    {
        Order.Remove(recordId);
        Skipped.Remove(recordId);
        Guaranteed.Remove(recordId);
        Weights.Remove(recordId);
    }
}

/// <summary>Format-1 only: a fixed 1-based position inside one round. Migrated into <see cref="OrchestrationRound.Order" />.</summary>
public sealed class OrchestrationPin
{
    public Guid RecordId { get; set; }

    public int Position { get; set; }
}

/// <summary>
///     Where the next draw continues: the active round of one profile. Persisted separately from the profile so a
///     draw and a settings edit never race on the same file.
/// </summary>
public sealed class OrchestrationRuntime
{
    public string ProfileId { get; set; } = string.Empty;

    public int Round { get; set; } = 1;

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
///     One participant as the ordering algorithm sees it: the already-resolved fair weight (RandomFirst semantics,
///     multiplied by any manual weight) plus the history figures the deferred tail is ordered by.
/// </summary>
public readonly record struct OrchestrationCandidate(
    Guid RecordId,
    double SamplingWeight,
    int DrawCount,
    DateTime LastDrawn,
    bool IsExcluded);