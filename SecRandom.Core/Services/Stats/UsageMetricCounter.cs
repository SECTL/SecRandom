using System.Globalization;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     Counts draw and launch events for the current day and hands out the increments that still have to
///     reach the SECTL statistics API.
///     Only the daily figure is reported: the service derives weekly, monthly, and lifetime totals from the
///     daily rows, so sending four periods per event only quadrupled the request count for values nobody
///     read. Pure state on purpose — the caller owns persistence and the network, so the counting rules
///     stay testable without any I/O.
/// </summary>
public sealed class UsageMetricCounter
{
    public const string RollCall = "rollCall";
    public const string Lottery = "lottery";
    public const string AppLaunch = "appLaunch";

    /// <summary>Event name to its single daily API field key.</summary>
    private static readonly Dictionary<string, string> DailyFieldKeys = new(StringComparer.Ordinal)
    {
        [RollCall] = "daily_roll_call_count",
        [Lottery] = "daily_lottery_count",
        [AppLaunch] = "daily_app_launch_count",
    };

    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<(string FieldKey, string Period), long> _pending = [];

    public UsageMetricCounter()
    {
    }

    private UsageMetricCounter(string day)
    {
        Day = day;
    }

    /// <summary>Day stamp the counters currently belong to.</summary>
    public string Day { get; private set; } = string.Empty;

    public static UsageMetricCounter FromSnapshot(UsageCounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var counter = new UsageMetricCounter(snapshot.Day);
        foreach (var (key, value) in snapshot.Counters)
            counter._counters[key] = value;
        return counter;
    }

    public UsageCounterSnapshot Snapshot() =>
        new(Day, new Dictionary<string, long>(_counters, StringComparer.Ordinal));

    /// <summary>Records one event for the current day and queues its single daily increment.</summary>
    public void Record(string eventName, DateTimeOffset now)
    {
        if (!DailyFieldKeys.TryGetValue(eventName, out var fieldKey))
            throw new ArgumentOutOfRangeException(nameof(eventName), eventName, "Unknown usage event.");

        var day = PeriodDaily(now);
        if (Day != day)
        {
            Day = day;
            _counters.Clear();
        }

        _counters[eventName] = _counters.GetValueOrDefault(eventName) + 1;
        var key = (fieldKey, day);
        _pending[key] = _pending.GetValueOrDefault(key) + 1;
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

    /// <summary>Today's count for one event.</summary>
    public long Count(string eventName) => _counters.GetValueOrDefault(eventName);

    public static string PeriodDaily(DateTimeOffset now) => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Field keys the API knows, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> KnownFieldKeys() => DailyFieldKeys.Values.ToArray();
}

/// <summary>Persisted shape of <see cref="UsageMetricCounter" />.</summary>
public sealed record UsageCounterSnapshot(string Day, IReadOnlyDictionary<string, long> Counters);

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
