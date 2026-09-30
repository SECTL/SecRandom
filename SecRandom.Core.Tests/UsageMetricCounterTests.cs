using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The usage counters feed the SECTL statistics API with the same field keys the v2 client has always
///     reported, so a v3 installation shows up in the same figures. Period rollover, ISO week stamps, and
///     retry restore are the parts worth pinning down.
/// </summary>
public sealed class UsageMetricCounterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void RollCallQueuesAllFourPeriodIncrements()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);

        var pending = counter.TakePending();
        Assert.Equal(4, pending.Count);
        Assert.Equal(1, pending[("daily_roll_call_count", "2026-09-30")]);
        Assert.Equal(1, pending[("weekly_roll_call_count", "2026-W40")]);
        Assert.Equal(1, pending[("monthly_roll_call_count", "2026-09")]);
        Assert.Equal(1, pending[("total_roll_call_count", "all")]);
    }

    [Fact]
    public void EachEventUsesItsOwnFieldKeys()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);
        counter.Record(UsageMetricCounter.AppLaunch, Noon);

        var pending = counter.TakePending();
        Assert.Equal(12, pending.Count);
        Assert.Equal(1, pending[("daily_lottery_count", "2026-09-30")]);
        Assert.Equal(1, pending[("total_lottery_count", "all")]);
        Assert.Equal(1, pending[("daily_app_launch_count", "2026-09-30")]);
        Assert.Equal(1, pending[("total_app_launch_count", "all")]);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall, "daily"));
        Assert.Equal(1, counter.Count(UsageMetricCounter.Lottery, "daily"));
    }

    [Fact]
    public void NewDayClearsDailyButKeepsLifetimeTotal()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.TakePending();

        counter.Record(UsageMetricCounter.RollCall, Noon.AddDays(1));

        // The API receives deltas, so one new event is always a delta of one per period.
        var pending = counter.TakePending();
        Assert.Equal(1, pending[("daily_roll_call_count", "2026-10-01")]);
        Assert.Equal(1, pending[("total_roll_call_count", "all")]);
        // The local counters are what carry the history forward.
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall, "daily"));
        Assert.Equal(3, counter.Count(UsageMetricCounter.RollCall, "total"));
    }

    [Fact]
    public void NewMonthClearsMonthlyAndKeepsWeekly()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.Lottery, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8)));
        counter.TakePending();

        counter.Record(UsageMetricCounter.Lottery, new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8)));

        var pending = counter.TakePending();
        Assert.Equal(1, pending[("monthly_lottery_count", "2026-10")]);
        Assert.Equal(1, pending[("weekly_lottery_count", "2026-W40")]);
        Assert.Equal(1, pending[("daily_lottery_count", "2026-10-01")]);
        Assert.Equal(1, counter.Count(UsageMetricCounter.Lottery, "monthly"));
        Assert.Equal(2, counter.Count(UsageMetricCounter.Lottery, "weekly"));
        Assert.Equal(2, counter.Count(UsageMetricCounter.Lottery, "total"));
    }

    [Fact]
    public void WeeklyStampFollowsIsoWeekYears()
    {
        // 2026 is a 53-week ISO year: its last days and the first days of 2027 share week 2026-W53.
        Assert.Equal("2026-W40", UsageMetricCounter.PeriodWeekly(Noon));
        Assert.Equal("2026-W53", UsageMetricCounter.PeriodWeekly(new DateTimeOffset(2027, 1, 1, 12, 0, 0, TimeSpan.FromHours(8))));
        Assert.Equal("2027-W01", UsageMetricCounter.PeriodWeekly(new DateTimeOffset(2027, 1, 4, 12, 0, 0, TimeSpan.FromHours(8))));
    }

    [Fact]
    public void FailedIncrementsCanBeRestoredForTheNextAttempt()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);

        var pending = counter.TakePending();
        Assert.Empty(counter.TakePending());

        counter.RestorePending(pending);
        counter.Record(UsageMetricCounter.RollCall, Noon);

        var retried = counter.TakePending();
        Assert.Equal(2, retried[("daily_roll_call_count", "2026-09-30")]);
        Assert.Equal(2, retried[("total_roll_call_count", "all")]);
    }

    [Fact]
    public void SnapshotRoundTripKeepsCountersAndPeriods()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.AppLaunch, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);

        var restored = UsageMetricCounter.FromSnapshot(counter.Snapshot());

        Assert.Equal("2026-09-30", restored.Day);
        Assert.Equal("2026-W40", restored.Week);
        Assert.Equal("2026-09", restored.Month);
        Assert.Equal(1, restored.Count(UsageMetricCounter.AppLaunch, "total"));
        Assert.Equal(1, restored.Count(UsageMetricCounter.Lottery, "monthly"));
        Assert.Equal(0, restored.Count(UsageMetricCounter.RollCall, "total"));
    }

    [Fact]
    public void UnknownEventIsRejectedInsteadOfSilentlyCounted()
    {
        var counter = new UsageMetricCounter();

        Assert.Throws<ArgumentOutOfRangeException>(() => counter.Record("rollcall", Noon));
    }

    [Fact]
    public void ReportedFieldKeysStayStable()
    {
        var keys = UsageMetricCounter.KnownFieldKeys();

        Assert.Contains("daily_roll_call_count", keys);
        Assert.Contains("weekly_roll_call_count", keys);
        Assert.Contains("monthly_roll_call_count", keys);
        Assert.Contains("total_roll_call_count", keys);
        Assert.Contains("daily_lottery_count", keys);
        Assert.Contains("daily_app_launch_count", keys);
        Assert.Equal(12, keys.Count);
    }

    [Fact]
    public void IncrementPayloadKeepsTheApiFieldNames()
    {
        // The v2 reporter posts snake_case, so camel-casing here would make every call fail validation and
        // the v3 counters would silently never arrive.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new UsageIncrementPayload("platform", "daily_roll_call_count", 2, "2026-09-30"),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("\"platform_id\":\"platform\"", json);
        Assert.Contains("\"field_key\":\"daily_roll_call_count\"", json);
        Assert.Contains("\"delta\":2", json);
        Assert.Contains("\"period\":\"2026-09-30\"", json);
        Assert.DoesNotContain("platformId", json);
        Assert.DoesNotContain("fieldKey", json);
    }
}
