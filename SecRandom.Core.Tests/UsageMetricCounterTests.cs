using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The usage counters feed the SECTL statistics API. Only the daily bucket travels and the increment payload
///     carries no time field, so the service side buckets every increment by its own receive time and owns the
///     weekly/monthly/lifetime figures.
/// </summary>
public sealed class UsageMetricCounterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void RollCallQueuesTheDailyFieldKeyOnly()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);

        var pending = counter.TakePending();
        Assert.Single(pending);
        Assert.Equal(1, pending["daily_roll_call_count"]);
    }

    [Fact]
    public void EachEventUsesItsOwnFieldKey()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);
        counter.Record(UsageMetricCounter.AppLaunch, Noon);

        var pending = counter.TakePending();
        Assert.Equal(3, pending.Count);
        Assert.Equal(1, pending["daily_roll_call_count"]);
        Assert.Equal(1, pending["daily_lottery_count"]);
        Assert.Equal(1, pending["daily_app_launch_count"]);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall));
    }

    [Fact]
    public void SameDayAccumulatesIntoOneIncrement()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon.AddMinutes(5));
        counter.Record(UsageMetricCounter.RollCall, Noon.AddHours(2));

        var pending = counter.TakePending();
        Assert.Single(pending);
        Assert.Equal(3, pending["daily_roll_call_count"]);
        Assert.Equal(3, counter.Count(UsageMetricCounter.RollCall));
    }

    [Fact]
    public void NewDayResetsTheLocalCountWithoutTouchingQueuedIncrements()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon);
        var beforeRollover = counter.TakePending();

        counter.Record(UsageMetricCounter.RollCall, Noon.AddDays(1));

        Assert.Equal("2026-10-01", counter.Day);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall));
        // 昨天排队等待上报的增量不因本地跨日而改变，分桶由服务端的接收时间决定
        Assert.Equal(2, beforeRollover["daily_roll_call_count"]);
        Assert.Equal(1, counter.TakePending()["daily_roll_call_count"]);
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
        Assert.Single(retried);
        Assert.Equal(2, retried["daily_roll_call_count"]);
    }

    [Fact]
    public void SnapshotRoundTripKeepsTheDayStampAndCounters()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.AppLaunch, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);

        var restored = UsageMetricCounter.FromSnapshot(counter.Snapshot());

        Assert.Equal("2026-09-30", restored.Day);
        Assert.Equal(1, restored.Count(UsageMetricCounter.AppLaunch));
        Assert.Equal(1, restored.Count(UsageMetricCounter.Lottery));
        Assert.Equal(0, restored.Count(UsageMetricCounter.RollCall));
    }

    [Fact]
    public void LegacyPeriodSuffixedCountersAreNotMistakenForTodaysCount()
    {
        // 旧状态文件按 "事件:周期" 保存，其中周/月/总是更粗的累计值，不能当成今天的计数读回来
        var legacy = new UsageCounterSnapshot("2026-09-30", new Dictionary<string, long>
        {
            ["rollCall:daily"] = 5,
            ["rollCall:total"] = 42,
        });

        var restored = UsageMetricCounter.FromSnapshot(legacy);

        Assert.Equal("2026-09-30", restored.Day);
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
        // 周/月/总不再由客户端上报，服务端按接收时间归属
        var keys = UsageMetricCounter.KnownFieldKeys();

        Assert.Equal(3, keys.Count);
        Assert.Contains("daily_roll_call_count", keys);
        Assert.Contains("daily_lottery_count", keys);
        Assert.Contains("daily_app_launch_count", keys);
        Assert.DoesNotContain("weekly_roll_call_count", keys);
        Assert.DoesNotContain("monthly_roll_call_count", keys);
        Assert.DoesNotContain("total_roll_call_count", keys);
    }

    [Fact]
    public void IncrementPayloadKeepsTheApiFieldNamesAndCarriesNoTime()
    {
        // The v2 reporter posts snake_case, so camel-casing here would make every call fail validation and
        // the v3 counters would silently never arrive.
        var json = System.Text.Json.JsonSerializer.Serialize(
            new UsageIncrementPayload("platform", "daily_roll_call_count", 2),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("\"platform_id\":\"platform\"", json);
        Assert.Contains("\"field_key\":\"daily_roll_call_count\"", json);
        Assert.Contains("\"delta\":2", json);
        Assert.DoesNotContain("platformId", json);
        Assert.DoesNotContain("fieldKey", json);
        // 服务端用接收时间分桶，载荷里既没有 period 也没有任何时间戳
        Assert.DoesNotContain("period", json);
        Assert.DoesNotContain("time", json);
    }
}
