using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.Stats;
using SecRandom.Shared;

namespace SecRandom.Services;

/// <summary>
///     Reports how many draws and launches this installation performs to the SECTL statistics API.
///     The v2 client has always sent this (`daily_roll_call_count` and friends), which is why the service-side
///     dashboards show traffic that the v3 client never contributed to; without it a v3 installation is
///     invisible in every usage figure. The field keys and report cadence match the v2 reporter so both
///     generations feed the same counters.
///     An increment carries no time at all: the service side stamps it with its own receive time, so the
///     client's clock and timezone can never move an event into another day, week, or month bucket.
///     Reporting is gated by <see cref="OnlineStatusMode.Off" /> and is always best-effort: no failure here
///     may ever reach a draw.
/// </summary>
public sealed class PlatformUsageReportService : IHostedService, IDisposable
{
    private const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string PlatformId = "69c8cd6a0012dd3ea10a";
    private static readonly Uri IncrementUri = new($"{ApiBaseUrl}/api/stats/usage/increment");
    // Draws arrive in bursts; a short delay merges them into one batch the way the v2 reporter does.
    private static readonly TimeSpan ReportDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly MainConfigHandler _configHandler;
    private readonly ILogger<PlatformUsageReportService> _logger;
    private readonly HttpClient _httpClient = new() { Timeout = RequestTimeout };
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _stateGate = new();
    private readonly string _statePath;
    private UsageMetricCounter _counter = new();
    private Timer? _timer;
    private bool _disposed;

    public PlatformUsageReportService(MainConfigHandler configHandler, ILogger<PlatformUsageReportService> logger)
    {
        _configHandler = configHandler;
        _logger = logger;
        _statePath = Utils.GetFilePath("config", "usage-stats.json");
        Load();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Record(UsageMetricCounter.AppLaunch, ReportDelay);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        _timer = null;
        await FlushAsync().ConfigureAwait(false);
    }

    public void RecordRollCall() => Record(UsageMetricCounter.RollCall, ReportDelay);

    public void RecordLottery() => Record(UsageMetricCounter.Lottery, ReportDelay);

    public void RecordAppLaunch() => Record(UsageMetricCounter.AppLaunch, ReportDelay);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        _flushGate.Dispose();
        _httpClient.Dispose();
    }

    private void Record(string eventName, TimeSpan delay)
    {
        if (_disposed)
            return;

        try
        {
            lock (_stateGate)
            {
                // The local time only stamps the day counter kept in this installation's own state file;
                // nothing time-related leaves the device.
                _counter.Record(eventName, DateTimeOffset.Now);
                Save();
            }
            _timer ??= new Timer(_ => _ = FlushAsync(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (Exception exception)
        {
            // A statistics failure must never surface in a draw.
            _logger.LogDebug(exception, "记录本地使用统计失败：{Event}", eventName);
        }
    }

    /// <summary>
    ///     Sends everything queued. A failed increment is put back so the next attempt still reports it,
    ///     while the increments that did succeed are not resent — unlike the v2 reporter, which replayed the
    ///     whole batch and inflated the counters whenever one call failed mid-batch.
    /// </summary>
    private async Task FlushAsync()
    {
        if (_disposed || IsReportingDisabled())
            return;

        if (!await _flushGate.WaitAsync(TimeSpan.Zero).ConfigureAwait(false))
            return;

        try
        {
            IReadOnlyDictionary<string, long> pending;
            lock (_stateGate)
                pending = _counter.TakePending();
            if (pending.Count == 0)
                return;

            Dictionary<string, long> failed = [];
            foreach (var (fieldKey, delta) in pending)
            {
                if (!await SendAsync(fieldKey, delta).ConfigureAwait(false))
                    failed[fieldKey] = delta;
            }

            if (failed.Count == 0)
                return;

            lock (_stateGate)
                _counter.RestorePending(failed);
            _logger.LogDebug("使用统计上报失败，{Count} 项增量将在下次重试", failed.Count);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "上报使用统计失败");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private async Task<bool> SendAsync(string fieldKey, long delta)
    {
        try
        {
            using var response = await _httpClient.PostAsync(
                IncrementUri,
                JsonContent.Create(
                    new UsageIncrementPayload(PlatformId, fieldKey, delta),
                    options: JsonOptions))
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return false;
        }
    }

    private bool IsReportingDisabled() =>
        _configHandler.Data.General.PrivacySettings.OnlineStatusMode == OnlineStatusMode.Off;

    private void Load()
    {
        try
        {
            if (!File.Exists(_statePath))
                return;

            var snapshot = JsonSerializer.Deserialize<UsageCounterSnapshot>(File.ReadAllText(_statePath), JsonOptions);
            if (snapshot is not null)
                _counter = UsageMetricCounter.FromSnapshot(snapshot);
        }
        catch (Exception exception)
        {
            // A damaged counter file only loses statistics, so start over instead of failing startup.
            _logger.LogDebug(exception, "读取本地使用统计失败，将从零开始");
        }
    }

    /// <summary>Atomic replace, matching every other persisted file in the app.</summary>
    private void Save()
    {
        var temporaryPath = _statePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(_counter.Snapshot(), JsonOptions));
        File.Move(temporaryPath, _statePath, overwrite: true);
    }
}
