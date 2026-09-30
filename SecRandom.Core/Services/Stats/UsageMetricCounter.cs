using System.Globalization;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     Counts draw and launch events for the current day, ISO week, month, and lifetime, and hands out the
///     increments that still have to reach the SECTL statistics API.
///     All four periods are reported from the client: the service side stores whatever period arrives and
///     does not derive the coarser ones, because the counters are shared by several platforms and clients
///     (v2 and v3) that each own their own field registry. Deriving them server-side as well would
///     double-count every event.
///     Pure state on purpose — the caller owns persistence and the network, so the counting rules stay
///     testable without any I/O.
/// </summary>
public sealed class UsageMetricCounter
{
    public const string RollCall = "rollCall";
    public const string Lottery = "lottery";
    public const string AppLaunch = "appLaunch";

    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Total = "total";

    /// <summary>Event name to the four API field keys, ordered daily, weekly, monthly, total.</summary>
    private static readonly Dictionary<string, string[]> FieldKeys = new(StringComparer.Ordinal)
    {
        [RollCall] =
        [
            "daily_roll_call_count",
            "weekly_roll_call_count",
            "monthly_roll_call_count",
            "total_roll_call_count",
        ],
        [Lottery] =
        [
            "daily_lottery_count",
            "weekly_lottery_count",
            "monthly_lottery_count",
            "total_lottery_count",
        ],
        [AppLaunch] =
        [
            "daily_app_launch_count",
            "weekly_app_launch_count",
            "monthly_app_launch_count",
            "total_app_launch_count",
        ],
    };

    private static readonly string[] PeriodKinds = [Daily, Weekly, Monthly, Total];

    private readonly Dictionary<(string EventName, string Kind), long> _counters = [];
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

    /// <summary>Day stamp the daily counters belong to.</summary>
    public string Day { get; private set; } = string.Empty;

    /// <summary>ISO week stamp the weekly counters belong to.</summary>
    public string Week { get; private set; } = string.Empty;

    /// <summary>Month stamp the monthly counters belong to.</summary>
    public string Month { get; private set; } = string.Empty;

    public static UsageMetricCounter FromSnapshot(UsageCounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var counter = new UsageMetricCounter(snapshot.Day, snapshot.Week, snapshot.Month);
        foreach (var (key, value) in snapshot.Counters)
        {
            var separator = key.LastIndexOf(':');
            if (separator <= 0) continue;
            counter._counters[(key[..separator], key[(separator + 1)..])] = value;
        }

        return counter;
    }

    public UsageCounterSnapshot Snapshot()
    {
        var counters = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var ((eventName, kind), value) in _counters)
            counters[$"{eventName}:{kind}"] = value;
        return new UsageCounterSnapshot(Day, Week, Month, counters);
    }

    /// <summary>Records one event for the current day/week/month and queues its four increments.</summary>
    public void Record(string eventName, DateTimeOffset now)
    {
        if (!FieldKeys.TryGetValue(eventName, out var fieldKeys))
            throw new ArgumentOutOfRangeException(nameof(eventName), eventName, "Unknown usage event.");

        RollOver(now);

        var periods = new[] { Day, Week, Month, "all" };
        for (var index = 0; index < PeriodKinds.Length; index += 1)
        {
            var kind = PeriodKinds[index];
            _counters[(eventName, kind)] = _counters.GetValueOrDefault((eventName, kind)) + 1;
            var key = (fieldKeys[index], periods[index]);
            _pending[key] = _pending.GetValueOrDefault(key) + 1;
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

    /// <summary>Current count for one event and period kind (daily by default).</summary>
    public long Count(string eventName, string kind = Daily) => _counters.GetValueOrDefault((eventName, kind));

    public static string PeriodDaily(DateTimeOffset now) =>
        now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>ISO week stamp, e.g. <c>2026-W40</c>; years are handled by the ISO rule itself.</summary>
    public static string PeriodWeekly(DateTimeOffset now)
    {
        var day = now.Date;
        var dayNumber = (int)day.DayOfWeek == 0 ? 7 : (int)day.DayOfWeek;
        var thursday = day.AddDays(4 - dayNumber);
        var firstThursday = new DateTime(thursday.Year, 1, 1);
        firstThursday = firstThursday.AddDays(
            ((int)firstThursday.DayOfWeek == 0 ? 7 : (int)firstThursday.DayOfWeek) <= 4
                ? 4 - ((int)firstThursday.DayOfWeek == 0 ? 7 : (int)firstThursday.DayOfWeek)
                : 11 - ((int)firstThursday.DayOfWeek == 0 ? 7 : (int)firstThursday.DayOfWeek));
        var week = 1 + (thursday - firstThursday).Days / 7;
        return $"{thursday.Year:D4}-W{week:D2}";
    }

    public static string PeriodMonthly(DateTimeOffset now) =>
        now.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Field keys the API knows, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> KnownFieldKeys() =>
        FieldKeys.Values.SelectMany(keys => keys).ToArray();

    /// <summary>Resets the counters of a period that has rolled over.</summary>
    private void ResetPeriod(string kind)
    {
        foreach (var eventName in FieldKeys.Keys)
            _counters.Remove((eventName, kind));
    }

    private void RollOver(DateTimeOffset now)
    {
        var day = PeriodDaily(now);
        if (Day != day)
        {
            if (Day.Length > 0) ResetPeriod(Daily);
            Day = day;
        }

        var week = PeriodWeekly(now);
        if (Week != week)
        {
            if (Week.Length > 0) ResetPeriod(Weekly);
            Week = week;
        }

        var month = PeriodMonthly(now);
        if (Month != month)
        {
            if (Month.Length > 0) ResetPeriod(Monthly);
            Month = month;
        }
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
