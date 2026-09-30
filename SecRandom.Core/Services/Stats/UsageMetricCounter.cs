using System.Globalization;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     Counts draw and launch events per day, ISO week, month, and all time, and hands out the increments
///     that still have to reach the SECTL statistics API.
///     Pure state on purpose: the caller owns persistence and the network, so the counting rules — period
///     rollover, ISO week boundaries, retry restore — stay testable without any I/O.
///     The field keys and period formats mirror the v2 reporter exactly, because both generations report
///     into the same counters on the service side.
/// </summary>
public sealed class UsageMetricCounter
{
    public const string RollCall = "rollCall";
    public const string Lottery = "lottery";
    public const string AppLaunch = "appLaunch";

    /// <summary>Event name to the four API field keys, ordered daily, weekly, monthly, total.</summary>
    private static readonly Dictionary<string, string[]> FieldKeys = new(StringComparer.Ordinal)
    {
        [RollCall] =
        [
            "daily_roll_call_count", "weekly_roll_call_count", "monthly_roll_call_count", "total_roll_call_count"
        ],
        [Lottery] =
        [
            "daily_lottery_count", "weekly_lottery_count", "monthly_lottery_count", "total_lottery_count"
        ],
        [AppLaunch] =
        [
            "daily_app_launch_count", "weekly_app_launch_count", "monthly_app_launch_count", "total_app_launch_count"
        ]
    };

    private static readonly string[] PeriodKinds = ["daily", "weekly", "monthly", "total"];

    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<(string FieldKey, string Period), long> _pending = [];

    public UsageMetricCounter()
    {
    }

    private UsageMetricCounter(string day, string week, string month)
    {
        Day = day;
        Week = week;
        Month = month;
    }

    /// <summary>Period stamps the counters currently belong to.</summary>
    public string Day { get; private set; } = string.Empty;

    public string Week { get; private set; } = string.Empty;
    public string Month { get; private set; } = string.Empty;

    public static UsageMetricCounter FromSnapshot(UsageCounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var counter = new UsageMetricCounter(snapshot.Day, snapshot.Week, snapshot.Month);
        foreach (var (key, value) in snapshot.Counters)
            counter._counters[key] = value;
        return counter;
    }

    public UsageCounterSnapshot Snapshot() => new(Day, Week, Month, new Dictionary<string, long>(_counters, StringComparer.Ordinal));

    /// <summary>Records one event and queues its four period increments.</summary>
    public void Record(string eventName, DateTimeOffset now)
    {
        if (!FieldKeys.TryGetValue(eventName, out var fieldKeys))
            throw new ArgumentOutOfRangeException(nameof(eventName), eventName, "Unknown usage event.");

        var periods = ResolvePeriods(now);
        RollOver(periods);

        for (var index = 0; index < PeriodKinds.Length; index++)
        {
            var kind = PeriodKinds[index];
            var fieldKey = fieldKeys[index];
            var period = kind switch
            {
                "daily" => periods.Daily,
                "weekly" => periods.Weekly,
                "monthly" => periods.Monthly,
                _ => "all"
            };
            _counters[CounterKey(eventName, kind)] = _counters.GetValueOrDefault(CounterKey(eventName, kind)) + 1;
            _pending[(fieldKey, period)] = _pending.GetValueOrDefault((fieldKey, period)) + 1;
        }
    }

    /// <summary>Removes and returns everything queued for the API.</summary>
    public IReadOnlyDictionary<(string FieldKey, string Period), long> TakePending()
    {
        var taken = new Dictionary<(string FieldKey, string Period), long>(_pending);
        _pending.Clear();
        return taken;
    }

    /// <summary>Puts a failed batch back so the next attempt still reports it.</summary>
    public void RestorePending(IReadOnlyDictionary<(string FieldKey, string Period), long> increments)
    {
        ArgumentNullException.ThrowIfNull(increments);
        foreach (var (key, delta) in increments)
            _pending[key] = _pending.GetValueOrDefault(key) + delta;
    }

    public long Count(string eventName, string periodKind) => _counters.GetValueOrDefault(CounterKey(eventName, periodKind));

    public static string PeriodDaily(DateTimeOffset now) => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>ISO week stamp. The ISO week-year can differ from the calendar year at both ends.</summary>
    public static string PeriodWeekly(DateTimeOffset now) =>
        $"{ISOWeek.GetYear(now.Date):0000}-W{ISOWeek.GetWeekOfYear(now.Date):00}";

    public static string PeriodMonthly(DateTimeOffset now) => now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Field keys the API knows, used to drop counters for events this build no longer reports.</summary>
    public static IReadOnlyCollection<string> KnownFieldKeys() => FieldKeys.Values.SelectMany(keys => keys).ToArray();

    private static string CounterKey(string eventName, string periodKind) => $"{eventName}|{periodKind}";

    private static (string Daily, string Weekly, string Monthly) ResolvePeriods(DateTimeOffset now) =>
        (PeriodDaily(now), PeriodWeekly(now), PeriodMonthly(now));

    /// <summary>
    ///     Clears the counters whose period has moved on. Totals are never reset, exactly like the reporter
    ///     this replaces: a new day must not wipe the lifetime figure.
    /// </summary>
    private void RollOver((string Daily, string Weekly, string Monthly) periods)
    {
        if (Day != periods.Daily)
        {
            Day = periods.Daily;
            ResetPeriod("daily");
        }

        if (Week != periods.Weekly)
        {
            Week = periods.Weekly;
            ResetPeriod("weekly");
        }

        if (Month == periods.Monthly)
            return;

        Month = periods.Monthly;
        ResetPeriod("monthly");
    }

    private void ResetPeriod(string periodKind)
    {
        foreach (var eventName in FieldKeys.Keys)
            _counters[CounterKey(eventName, periodKind)] = 0;
    }
}

/// <summary>Persisted shape of <see cref="UsageMetricCounter" />.</summary>
public sealed record UsageCounterSnapshot(
    string Day,
    string Week,
    string Month,
    IReadOnlyDictionary<string, long> Counters);

/// <summary>
///     One increment as the SECTL statistics API expects it. The names are the API's (`platform_id`,
///     `field_key`), not the app's: camel-casing them makes every call fail validation and the counters
///     silently never arrive, so they are pinned here and covered by a test.
/// </summary>
public sealed record UsageIncrementPayload(
    [property: JsonPropertyName("platform_id")] string PlatformId,
    [property: JsonPropertyName("field_key")] string FieldKey,
    [property: JsonPropertyName("delta")] long Delta,
    [property: JsonPropertyName("period")] string Period);
