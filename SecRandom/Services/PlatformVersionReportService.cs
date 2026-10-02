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
///     (`POST /api/stats/version`), which counts people per version: the service dedups by identity — the
///     signed-in account id when there is one, otherwise the pseudo-anonymous device UUID — and keeps one current
///     version per identity. Nothing else can produce that figure: `/api/fields/values` keeps only the last
///     reporter's value and `/api/stats/usage/increment` counts reports, not people.
///     Reporting follows what the API asks for: once per start and once more whenever the effective identity
///     changes (sign-in, sign-out), never on a poll.
///     Unlike the online-status and usage-counter channels this one is deliberately **not** gated by
///     <c>PrivacySettings.OnlineStatusMode</c>: the version mix is the project's own release baseline, so it keeps
///     reporting while location/activity reporting is off. The payload stays minimal for that reason — the version
///     plus exactly one identity, never an address or region — and reporting is always best-effort, so no
///     statistics failure can reach startup, a draw, or the account flow.
/// </summary>
public sealed class PlatformVersionReportService : BackgroundService
{
    private const string ApiBaseUrl = "https://appwrite.sectl.cn";
    private const string PlatformId = "69c8cd6a0012dd3ea10a";
    private static readonly Uri VersionReportUri = new($"{ApiBaseUrl}/api/stats/version");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly DeviceUuidStore _deviceUuidStore;
    private readonly IStatsAccountIdentitySource _identitySource;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PlatformVersionReportService> _logger;
    private readonly object _stateGate = new();
    // 启动时的第一次上报就是这里的初始信号，之后每次身份变化再补一次；满队列时只保留一次待上报，
    // 服务端按身份去重，合并成一次上报即可，也不会触发 600 次/分钟的限流。
    private readonly SemaphoreSlim _reportSignal = new(1, 1);
    private string? _reportedIdentity;

    public PlatformVersionReportService(
        DeviceUuidStore deviceUuidStore,
        IStatsAccountIdentitySource identitySource,
        IHttpClientFactory httpClientFactory,
        ILogger<PlatformVersionReportService> logger)
    {
        _deviceUuidStore = deviceUuidStore;
        _identitySource = identitySource;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        _identitySource.UserIdChanged += IdentitySourceOnUserIdChanged;
        return base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _identitySource.UserIdChanged -= IdentitySourceOnUserIdChanged;
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public override void Dispose()
    {
        _identitySource.UserIdChanged -= IdentitySourceOnUserIdChanged;
        _reportSignal.Dispose();
        base.Dispose();
    }

    /// <summary>
    ///     Hands the reporting loop to the thread pool. <c>Host.StartAsync</c> runs a hosted service's
    ///     <c>ExecuteAsync</c> synchronously up to its first incomplete await, and this loop's synchronous prefix
    ///     reads (and on first run writes) the device UUID file and builds the HTTP request — none of that may run
    ///     on the thread that is starting the application, and none of the reporting may delay it either.
    /// </summary>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => RunAsync(stoppingToken), CancellationToken.None);

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("版本使用人数上报服务已启动");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await _reportSignal.WaitAsync(stoppingToken).ConfigureAwait(false);
                await ReportOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _logger.LogDebug("版本使用人数上报服务已停止");
        }
    }

    private async Task ReportOnceAsync(CancellationToken cancellationToken)
    {
        VersionUsageReportPayload payload;
        try
        {
            payload = VersionUsageReportPayload.Create(
                PlatformId,
                // 版本号保留构建产物自带的 v 前缀：控制台按这个字符串归并人数，不要改成 Tag 或去掉前缀
                GlobalConstants.Version,
                _identitySource.UserId,
                _deviceUuidStore.GetOrCreate().ToString("D").ToLowerInvariant());
        }
        catch (ArgumentException exception)
        {
            // 版本号格式或身份不合规只影响统计，绝不能影响启动与抽奖
            _logger.LogDebug(exception, "版本使用人数上报内容无效，已跳过");
            return;
        }

        // 身份与版本都没变时，服务端已经记过同一个人的同一个版本，不再重复上报
        if (payload.Identity is not { Length: > 0 } identity || !BeginReport(identity))
            return;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            using var response = await _httpClientFactory.CreateClient()
                .PostAsync(VersionReportUri, JsonContent.Create(payload, options: JsonOptions), timeout.Token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 失败不做重试轰炸：下一次启动或下一次身份变化仍会补报
                _logger.LogDebug("版本使用人数上报被拒绝：HTTP {StatusCode}", (int)response.StatusCode);
                return;
            }

            _logger.LogDebug("已上报版本使用人数：{Version}（{Identity}）", payload.Version, identity);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "版本使用人数上报失败");
        }
    }

    private bool BeginReport(string identity)
    {
        lock (_stateGate)
        {
            if (string.Equals(_reportedIdentity, identity, StringComparison.Ordinal))
                return false;

            _reportedIdentity = identity;
            return true;
        }
    }

    private void IdentitySourceOnUserIdChanged(object? sender, EventArgs e)
    {
        try
        {
            _reportSignal.Release();
        }
        catch (SemaphoreFullException)
        {
            // 已经有一次上报排队，它会读到最新身份，无需叠加
        }
    }
}
