using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The usage counters feed the SECTL statistics API. The client reports all four periods
///     (daily/weekly/monthly/total): the service side stores whatever arrives and does not derive the
///     coarser ones, because the counters are shared by several platforms and by the v2 client that
///     already reports four periods — deriving them there as well would double-count every event.
/// </summary>
public sealed class UsageMetricCounterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void RollCallQueuesAllFourPeriods()
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
        Assert.Equal(1, pending[("total_app_launch_count", "all")]);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall));
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall, UsageMetricCounter.Weekly));
    }

    [Fact]
    public void SamePeriodAccumulatesIntoOneIncrementPerPeriod()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon.AddMinutes(5));
        counter.Record(UsageMetricCounter.RollCall, Noon.AddHours(2));

        var pending = counter.TakePending();
        Assert.Equal(4, pending.Count);
        Assert.All(pending.Values, value => Assert.Equal(3, value));
        Assert.Equal(3, counter.Count(UsageMetricCounter.RollCall));
    }

    [Fact]
    public void NewDayResetsDailyOnlyWhileWeekAndMonthKeepCounting()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.TakePending();

        // 次日仍在本周（2026-09-28 起为 W40）也仍在本月
        counter.Record(UsageMetricCounter.RollCall, Noon.AddDays(1));

        var pending = counter.TakePending();
        Assert.Equal(1, pending[("daily_roll_call_count", "2026-10-01")]);
        Assert.Equal(1, pending[("weekly_roll_call_count", "2026-W40")]);
        Assert.Equal(1, pending[("monthly_roll_call_count", "2026-10")]);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall));
        Assert.Equal(3, counter.Count(UsageMetricCounter.RollCall, UsageMetricCounter.Weekly));
    }

    [Fact]
    public void NewIsoWeekResetsTheWeeklyCounter()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.TakePending();

        // 2026-10-05 是下一周的周一
        counter.Record(UsageMetricCounter.RollCall, Noon.AddDays(5));

        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall, UsageMetricCounter.Weekly));
        // 10-05 已经跨月，月度计数同样重新开始
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall, UsageMetricCounter.Monthly));
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
    public void SnapshotRoundTripKeepsPeriodStampsAndCounters()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.AppLaunch, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);

        var restored = UsageMetricCounter.FromSnapshot(counter.Snapshot());

        Assert.Equal("2026-09-30", restored.Day);
        Assert.Equal("2026-W40", restored.Week);
        Assert.Equal("2026-09", restored.Month);
        Assert.Equal(1, restored.Count(UsageMetricCounter.AppLaunch));
        Assert.Equal(1, restored.Count(UsageMetricCounter.Lottery, UsageMetricCounter.Monthly));
        Assert.Equal(0, restored.Count(UsageMetricCounter.RollCall));
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
        // 客户端照旧上报四个周期，服务端不做派生：两边都做会重复计数
        var keys = UsageMetricCounter.KnownFieldKeys();

        Assert.Equal(12, keys.Count);
        Assert.Contains("daily_roll_call_count", keys);
        Assert.Contains("weekly_roll_call_count", keys);
        Assert.Contains("monthly_roll_call_count", keys);
        Assert.Contains("total_roll_call_count", keys);
        Assert.Contains("total_app_launch_count", keys);
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
