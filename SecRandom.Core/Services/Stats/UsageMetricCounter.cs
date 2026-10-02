using System.Globalization;
using System.Text.Json.Serialization;

namespace SecRandom.Core.Services.Stats;

/// <summary>
///     Counts draw and launch events for the current local day and hands out the daily increments that still
///     have to reach the SECTL statistics API.
///     Only the daily bucket travels, and the increment payload carries no time field at all: the service side
///     buckets every increment by the time it receives it and owns the weekly/monthly/lifetime figures. Sending
///     a client-side period stamp would only let the client's clock and timezone disagree with the service over
///     which bucket an event belongs to.
///     Pure state on purpose — the caller owns persistence and the network, so the counting rules stay
///     testable without any I/O.
/// </summary>
public sealed class UsageMetricCounter
{
    public const string RollCall = "rollCall";
    public const string Lottery = "lottery";
    public const string AppLaunch = "appLaunch";

    /// <summary>Event name to the reported API field key.</summary>
    private static readonly Dictionary<string, string> FieldKeys = new(StringComparer.Ordinal)
    {
        [RollCall] = "daily_roll_call_count",
        [Lottery] = "daily_lottery_count",
        [AppLaunch] = "daily_app_launch_count",
    };

    private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _pending = new(StringComparer.Ordinal);

    public UsageMetricCounter()
    {
    }

    private UsageMetricCounter(string day)
    {
        Day = day;
    }

    /// <summary>Day stamp the local counters belong to.</summary>
    public string Day { get; private set; } = string.Empty;

    public static UsageMetricCounter FromSnapshot(UsageCounterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var counter = new UsageMetricCounter(snapshot.Day);
        foreach (var (key, value) in snapshot.Counters)
        {
            // Files written while the client still sent a period stamp keyed their counters as "event:period".
            // Only known event names are the current day's count, so those legacy keys are dropped instead of
            // being mistaken for today when their value was a weekly or lifetime figure.
            if (FieldKeys.ContainsKey(key))
                counter._counters[key] = value;
        }

        return counter;
    }

    public UsageCounterSnapshot Snapshot()
    {
        var counters = new Dictionary<string, long>(_counters, StringComparer.Ordinal);
        return new UsageCounterSnapshot(Day, counters);
    }

    /// <summary>Records one event for the current local day and queues its increment.</summary>
    public void Record(string eventName, DateTimeOffset now)
    {
        if (!FieldKeys.TryGetValue(eventName, out var fieldKey))
            throw new ArgumentOutOfRangeException(nameof(eventName), eventName, "Unknown usage event.");

        RollOver(now);

        _counters[eventName] = _counters.GetValueOrDefault(eventName) + 1;
        _pending[fieldKey] = _pending.GetValueOrDefault(fieldKey) + 1;
    }

    /// <summary>Removes and returns everything queued for the API.</summary>
    public IReadOnlyDictionary<string, long> TakePending()
    {
        var taken = new Dictionary<string, long>(_pending, StringComparer.Ordinal);
        _pending.Clear();
        return taken;
    }

    /// <summary>Puts a failed batch back so the next attempt still reports it.</summary>
    public void RestorePending(IReadOnlyDictionary<string, long> increments)
    {
        ArgumentNullException.ThrowIfNull(increments);
        foreach (var (fieldKey, delta) in increments)
            _pending[fieldKey] = _pending.GetValueOrDefault(fieldKey) + delta;
    }

    /// <summary>Current count for one event.</summary>
    public long Count(string eventName) => _counters.GetValueOrDefault(eventName);

    /// <summary>Field keys the API knows, for diagnostics and tests.</summary>
    public static IReadOnlyCollection<string> KnownFieldKeys() => FieldKeys.Values.ToArray();

    private static string DayStamp(DateTimeOffset now) =>
        now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Resets the local counters when the local day has rolled over.</summary>
    private void RollOver(DateTimeOffset now)
    {
        var day = DayStamp(now);
        if (Day == day)
            return;

        if (Day.Length > 0)
            _counters.Clear();
        Day = day;
    }
}

/// <summary>Persisted shape of <see cref="UsageMetricCounter" />.</summary>
public sealed record UsageCounterSnapshot(
    string Day,
    IReadOnlyDictionary<string, long> Counters);

/// <summary>
///     One increment as the SECTL statistics API expects it. The names are the API's (`platform_id`,
///     `field_key`), not the app's: camel-casing them makes every call fail validation and the counters
///     silently never arrive, so they are pinned here and covered by a test.
///     There is deliberately no time field: the service side stamps each increment with its own receive time.
/// </summary>
public sealed record UsageIncrementPayload(
    [property: JsonPropertyName("platform_id")] string PlatformId,
    [property: JsonPropertyName("field_key")] string FieldKey,
    [property: JsonPropertyName("delta")] long Delta);
