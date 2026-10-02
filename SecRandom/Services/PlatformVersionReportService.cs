using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecRandom.Core;
using SecRandom.Core.Services.Stats;
using SecRandom.Services.Config;

namespace SecRandom.Services;

/// <summary>
///     Reports which version this installation runs to the SECTL version-usage counter
///     (`POST /api/stats/version`), which counts people per version: the service dedups by identity and this client
///     sends only the pseudo-anonymous device UUID — never the SECTL account id — so a version figure can never be
///     tied to an account. Nothing else can produce that figure: `/api/fields/values` keeps only the last
///     reporter's value and `/api/stats/usage/increment` counts reports, not people.
///     The API asks for one report per start, so this runs exactly once per process: no polling, no retry storm,
///     and no state to keep. Unlike the online-status and usage-counter channels it is deliberately **not** gated by
///     <c>PrivacySettings.OnlineStatusMode</c> — the version mix is the project's own release baseline, which is
///     also why its payload stays minimal. Reporting is always best-effort and runs on the thread pool, so neither
///     a statistics failure nor the statistics work itself can reach startup, a draw, or the account flow.
/// </summary>
public sealed class PlatformVersionReportService : BackgroundService
{
    private const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string PlatformId = "69c8cd6a0012dd3ea10a";
    private static readonly Uri VersionReportUri = new($"{ApiBaseUrl}/api/stats/version");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DeviceUuidStore _deviceUuidStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlatformVersionReportService> _logger;

    public PlatformVersionReportService(
        DeviceUuidStore deviceUuidStore,
        IHttpClientFactory httpClientFactory,
        ILogger<PlatformVersionReportService> logger)
    {
        _deviceUuidStore = deviceUuidStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    ///     Hands the report to the thread pool. <c>Host.StartAsync</c> runs a hosted service's <c>ExecuteAsync</c>
    ///     synchronously up to its first incomplete await, and this report's synchronous prefix reads (and on first
    ///     run writes) the device UUID file and builds the HTTP request — none of that may run on the thread that is
    ///     starting the application, and the request may not delay it either.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => ReportOnceAsync(stoppingToken), CancellationToken.None);

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        VersionUsageReportPayload payload;
        try
        {
            payload = VersionUsageReportPayload.Create(
                PlatformId,
                // 版本号保留构建产物自带的 v 前缀：控制台按这个字符串归并人数，不要改成 Tag 或去掉前缀
                GlobalConstants.Version,
                _deviceUuidStore.GetOrCreate().ToString("D"));
        }
        catch (ArgumentException exception)
        {
            // 版本号格式或设备标识不合规只影响统计，绝不能影响启动与抽奖
            _logger.LogDebug(exception, "版本使用人数上报内容无效，已跳过");
            return;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClientFactory.CreateClient()
                .PostAsync(VersionReportUri, JsonContent.Create(payload, options: JsonOptions), timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 失败不做重试轰炸：下一次启动仍会上报一次
                _logger.LogDebug("版本使用人数上报被拒绝：HTTP {StatusCode}", (int)response.StatusCode);
                return;
            }

            _logger.LogDebug("已上报版本使用人数：{Version}", payload.Version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "版本使用人数上报失败");
        }
    }
}
