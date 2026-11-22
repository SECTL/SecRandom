using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Interfaces;
using SecRandom.Core.Models.Orchestration;
using SecRandom.Core.Services.Orchestration;
using SecRandom.Shared.Models.Profile;
using Xunit;

namespace SecRandom.Core.Tests;

/// <summary>
///     编排抽取的顺序解析：一轮是一条队列，五份名单是一轮里的五种规则，而不是五份互相覆盖的绝对位置表。
/// </summary>
public class OrchestrationOrderingTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedRandomSource(double value) : IRandomSource
    {
        public int NextInt32(int maxExclusive) => maxExclusive <= 0 ? 0 : Math.Min(maxExclusive - 1, (int)(value * maxExclusive));

        public double NextDouble() => value;
    }

    private static Guid Id(int seed) => new(seed, 0, 0, new byte[8]);

    private static OrchestrationCandidate Candidate(int seed, double weight = 1d, int count = 0) =>
        new(Id(seed), weight, count, Base.AddDays(-count), false);

    private static OrchestrationRound Round(int index = 1) => new() { Index = index };

    private static OrchestrationRules Rules() => new();

    private static IReadOnlyList<Guid> Resolve(
        IReadOnlyList<OrchestrationCandidate> candidates,
        OrchestrationRound round,
        OrchestrationRules? rules = null,
        IReadOnlySet<Guid>? drawn = null) =>
        OrchestrationOrdering.Resolve(
            candidates, round, drawn ?? new HashSet<Guid>(), new FixedRandomSource(0.5), rules ?? Rules());

    [Fact]
    public void CountModeExpandsOnlyRemainingInventoryUnits()
    {
        var prize = new Prize { RecordId = Id(1), Name = "Ticket", Count = 3 };
        var profile = new OrchestrationProfile
        {
            Target = OrchestrationTarget.Lottery,
            Members = [new OrchestrationMember { RecordId = prize.RecordId, Name = prize.Name }]
        };
        var round = Round();
        round.Order = [prize.RecordId];

        var scheduler = new OrchestrationScheduler(null!);
        var remaining = scheduler.OrderRound(
            profile,
            round,
            [prize],
            new Dictionary<Guid, int> { [prize.RecordId] = 1 },
            new FixedRandomSource(0.5),
            expandInventory: true);

        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, item => Assert.Same(prize, item));
    }

    [Fact]
    public void CountModeDoesNotExpandWhenInventoryIsExhausted()
    {
        var prize = new Prize { RecordId = Id(1), Name = "Ticket", Count = 2 };
        var profile = new OrchestrationProfile
        {
            Target = OrchestrationTarget.Lottery,
            Members = [new OrchestrationMember { RecordId = prize.RecordId, Name = prize.Name }]
        };

        var scheduler = new OrchestrationScheduler(null!);
        var remaining = scheduler.OrderRound(
            profile,
            Round(),
            [prize],
            new Dictionary<Guid, int> { [prize.RecordId] = 2 },
            new FixedRandomSource(0.5),
            expandInventory: true);

        Assert.Empty(remaining);
    }

    [Fact]
    public void QueueIsFollowedAndTheDrawnMemberLeavesSoTheNextOneMovesUp()
    {
        var candidates = new[] { Candidate(1), Candidate(2), Candidate(3) };
        var round = Round();
        round.Order = [Id(1), Id(2), Id(3)];

        var order = Resolve(candidates, round);
        Assert.Equal([Id(1), Id(2), Id(3)], order);

        // 抽掉第一个人之后重新解析：后面的人前移，而不是重新在"剩余三个人"里坐第 3 个位置。
        var afterFirst = Resolve(candidates, round, drawn: new HashSet<Guid> { Id(1) });
        Assert.Equal([Id(2), Id(3)], afterFirst);

        var afterSecond = Resolve(candidates, round, drawn: new HashSet<Guid> { Id(1), Id(2) });
        Assert.Equal([Id(3)], afterSecond);
    }

    [Fact]
    public void QueueSkipsMembersThatLeftTheRound()
    {
        var round = Round();
        round.Order = [Id(1), Id(2), Id(3), Id(4)];
        round.Skipped.Add(Id(2));

        var order = Resolve([Candidate(1), Candidate(2), Candidate(3), Candidate(4)], round);

        Assert.Equal([Id(1), Id(3), Id(4)], order);
    }

    [Fact]
    public void MembersOutsideTheQueueComeAfterItByWeight()
    {
        var round = Round();
        round.Order = [Id(3)];

        var order = Resolve([Candidate(1), Candidate(2), Candidate(3)], round);

        Assert.Equal(3, order.Count);
        Assert.Equal(Id(3), order[0]);
        Assert.Equal([Id(1), Id(2)], order.Skip(1).OrderBy(id => id));
    }

    [Fact]
    public void GuaranteedMembersLeadTheRound()
    {
        var round = Round();
        round.Guaranteed.Add(Id(3));
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Guarantee, true);

        var order = OrchestrationOrdering.Resolve(
            [Candidate(1), Candidate(2), Candidate(3)], round, new HashSet<Guid>(), new FixedRandomSource(0.5),
            rules);

        Assert.Equal(Id(3), order[0]);
        Assert.Equal(3, order.Count);
        Assert.Equal(3, order.Distinct().Count());
    }

    [Fact]
    public void GuaranteedGroupKeepsItsWeightOrder()
    {
        var round = Round();
        round.Guaranteed = [Id(1), Id(2)];
        round.Weights[Id(1)] = 5d;
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Guarantee, true);

        var order = Resolve([Candidate(1), Candidate(2), Candidate(3)], round, rules);

        // 必中名单内部仍然按权重排序：权重大的先出场，权重相同才看加入次序。
        Assert.Equal([Id(1), Id(2)], order.Take(2));
    }

    [Fact]
    public void GuaranteedWinsWhenAHandEditedFileCarriesBothFrontLists()
    {
        var round = Round();
        round.Order = [Id(2)];
        round.Guaranteed.Add(Id(3));
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Guarantee, true);

        var order = Resolve([Candidate(1), Candidate(2), Candidate(3)], round, rules);

        Assert.Equal(Id(3), order[0]);
        Assert.Equal(3, order.Count);
        Assert.Equal(3, order.Distinct().Count());
    }

    [Fact]
    public void ExcludedSkippedAndAlreadyDrawnMembersLeaveTheRound()
    {
        var round = Round();
        round.Skipped.Add(Id(2));
        var excluded = new OrchestrationCandidate(Id(3), 1d, 0, Base, true);

        var order = Resolve(
            [Candidate(1), Candidate(2), excluded, Candidate(4)], round, drawn: new HashSet<Guid> { Id(4) });

        Assert.Equal([Id(1)], order);
    }

    [Fact]
    public void ExcludedMembersComeBackWhenTheExcludeListIsSwitchedOff()
    {
        var excluded = new OrchestrationCandidate(Id(3), 1d, 0, Base, true);
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Exclude, false);

        var order = Resolve([Candidate(1), excluded], Round(), rules);

        Assert.Equal([Id(1), Id(3)], order.OrderBy(id => id));
    }

    [Fact]
    public void DeferredMembersComeBackWhenTheDeferListIsSwitchedOff()
    {
        var round = Round();
        round.Order = [Id(2), Id(1)];
        round.Skipped.Add(Id(2));
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Defer, false);

        var order = Resolve([Candidate(1), Candidate(2)], round, rules);

        Assert.Equal([Id(2), Id(1)], order);
    }

    [Fact]
    public void ZeroWeightMembersAreDeferredInsteadOfDropped()
    {
        var order = Resolve(
            [Candidate(1, weight: 0d, count: 3), Candidate(2, weight: 0d, count: 1), Candidate(3, weight: 1d)],
            Round());

        Assert.Equal(3, order.Count);
        Assert.Equal(Id(3), order[0]);
        // The deferred tail is ordered by draw count, most-drawn first: the ceiling protects nobody by omission.
        Assert.Equal(Id(1), order[1]);
        Assert.Equal(Id(2), order[2]);
    }

    [Fact]
    public void ManualWeightIsAppliedAsAMultiplier()
    {
        var round = Round();
        round.Weights[Id(1)] = 0d;

        var order = Resolve([Candidate(1, weight: 5d), Candidate(2, weight: 1d)], round);

        // Weight 0 does not ban the member, it moves it behind the weighted fill.
        Assert.Equal(Id(2), order[0]);
        Assert.Equal(Id(1), order[1]);
    }

    [Fact]
    public void ManualWeightIsIgnoredWhenTheWeightListIsSwitchedOff()
    {
        var round = Round();
        round.Weights[Id(1)] = 0d;
        var rules = Rules();
        rules.SetEnabled(OrchestrationRuleKind.Weight, false);

        var order = Resolve([Candidate(1, weight: 5d), Candidate(2, weight: 1d)], round, rules);

        // 关掉权重名单后手动值留在数据里但不参与：Id(1) 的采样权重更高，先出场。
        Assert.Equal(Id(1), order[0]);
        Assert.Equal(Id(2), order[1]);
    }

    [Fact]
    public void WeightedFillUsesEveryRemainingCandidateExactlyOnce()
    {
        var candidates = Enumerable.Range(1, 20).Select(seed => Candidate(seed, weight: seed)).ToList();

        var order = Resolve(candidates, Round());

        Assert.Equal(candidates.Count, order.Count);
        Assert.Equal(candidates.Select(candidate => candidate.RecordId).OrderBy(id => id),
            order.OrderBy(id => id));
    }

    [Fact]
    public void RoundCountIsClampedAndRulesAreFilledInWhenLoadingAnOldProfile()
    {
        var profile = new OrchestrationProfile
        {
            Id = "abc",
            RoundCount = 99,
            Members = [new OrchestrationMember { RecordId = Id(1), Number = "1", Name = "A" }],
            Rounds =
            [
                new OrchestrationRound { Index = 3, Order = [Id(1)], LegacyPins = [new OrchestrationPin { RecordId = Id(1), Position = 1 }] },
                new OrchestrationRound { Index = 42 }
            ]
        };
        profile.Rules = null!;

        OrchestrationStore.Normalize(profile);

        Assert.Equal(OrchestrationProfile.CurrentFormatVersion, profile.FormatVersion);
        Assert.Equal(10, profile.EffectiveRoundCount);
        Assert.Equal([3], profile.Rounds.Select(round => round.Index));
        Assert.NotNull(profile.Rules);
        Assert.True(profile.Rules.Order);
        Assert.False(profile.Rules.Guarantee);
    }

    [Fact]
    public void LegacyPinsBecomeTheRoundQueueInPinOrder()
    {
        var profile = new OrchestrationProfile
        {
            Id = "abc",
            RoundCount = 2,
            Members =
            [
                new OrchestrationMember { RecordId = Id(1), Number = "1", Name = "A" },
                new OrchestrationMember { RecordId = Id(2), Number = "2", Name = "B" },
                new OrchestrationMember { RecordId = Id(3), Number = "3", Name = "C" }
            ],
            Rounds =
            [
                new OrchestrationRound
                {
                    Index = 1,
                    Order = [Id(1)],
                    LegacyPins = [new OrchestrationPin { RecordId = Id(3), Position = 1 }, new OrchestrationPin { RecordId = Id(2), Position = 2 }]
                }
            ]
        };

        OrchestrationStore.Normalize(profile);

        var round = profile.GetRound(1);
        // 旧 pin 的绝对槽位不保留，只保留它表达的相对次序：3 → 2，再接原来的副本 1。
        Assert.Equal([Id(3), Id(2), Id(1)], round.Order);
        Assert.Null(round.LegacyPins);
    }

    [Fact]
    public void RoundListsOnlyKeepMembersThatStillExist()
    {
        var profile = new OrchestrationProfile
        {
            Id = "abc",
            Members = [new OrchestrationMember { RecordId = Id(1), Number = "1", Name = "A" }],
            Rounds =
            [
                new OrchestrationRound
                {
                    Index = 1,
                    Order = [Id(1), Id(9)],
                    Skipped = [Id(9)],
                    Guaranteed = [Id(9)],
                    Weights = new Dictionary<Guid, double> { [Id(9)] = 50d }
                }
            ]
        };

        OrchestrationStore.Normalize(profile);

        var round = profile.GetRound(1);
        Assert.Equal([Id(1)], round.Order);
        Assert.Empty(round.Skipped);
        Assert.Empty(round.Guaranteed);
        Assert.Empty(round.Weights);
        Assert.Equal(OrchestrationTarget.RollCall, profile.Target);
        Assert.True(profile.Rules.Exclude);
    }
}