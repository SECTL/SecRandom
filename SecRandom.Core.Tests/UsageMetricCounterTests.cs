using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The usage counters feed the SECTL statistics API. Only the daily figure is reported: the service
///     derives weekly/monthly/lifetime totals from the daily rows, so one request per event is enough.
/// </summary>
public sealed class UsageMetricCounterTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public void RollCallQueuesExactlyOneDailyIncrement()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);

        var pending = counter.TakePending();
        var increment = Assert.Single(pending);
        Assert.Equal("daily_roll_call_count", increment.Key.FieldKey);
        Assert.Equal("2026-09-30", increment.Key.Period);
        Assert.Equal(1, increment.Value);
    }

    [Fact]
    public void EachEventUsesItsOwnDailyFieldKey()
    {
        var counter = new UsageMetricCounter();

        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.Lottery, Noon);
        counter.Record(UsageMetricCounter.AppLaunch, Noon);

        var pending = counter.TakePending();
        Assert.Equal(3, pending.Count);
        Assert.Equal(1, pending[("daily_roll_call_count", "2026-09-30")]);
        Assert.Equal(1, pending[("daily_lottery_count", "2026-09-30")]);
        Assert.Equal(1, pending[("daily_app_launch_count", "2026-09-30")]);
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
        var increment = Assert.Single(pending);
        Assert.Equal(3, increment.Value);
        Assert.Equal(3, counter.Count(UsageMetricCounter.RollCall));
    }

    [Fact]
    public void NewDayStartsFromOneAndForgetsYesterday()
    {
        var counter = new UsageMetricCounter();
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.Record(UsageMetricCounter.RollCall, Noon);
        counter.TakePending();

        counter.Record(UsageMetricCounter.RollCall, Noon.AddDays(1));

        var pending = counter.TakePending();
        var increment = Assert.Single(pending);
        Assert.Equal("2026-10-01", increment.Key.Period);
        Assert.Equal(1, increment.Value);
        Assert.Equal(1, counter.Count(UsageMetricCounter.RollCall));
        Assert.Equal("2026-10-01", counter.Day);
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
    }

    [Fact]
    public void SnapshotRoundTripKeepsTheDayAndCounters()
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
    public void UnknownEventIsRejectedInsteadOfSilentlyCounted()
    {
        var counter = new UsageMetricCounter();

        Assert.Throws<ArgumentOutOfRangeException>(() => counter.Record("rollcall", Noon));
    }

    [Fact]
    public void ReportedFieldKeysStayStable()
    {
        // 只报每日：周/月/总由服务端从每日推，客户端不再多发三条请求
        var keys = UsageMetricCounter.KnownFieldKeys();

        Assert.Equal(3, keys.Count);
        Assert.Contains("daily_roll_call_count", keys);
        Assert.Contains("daily_lottery_count", keys);
        Assert.Contains("daily_app_launch_count", keys);
        Assert.DoesNotContain("weekly_roll_call_count", keys);
        Assert.DoesNotContain("total_roll_call_count", keys);
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
