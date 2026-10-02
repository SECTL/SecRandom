using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services;
using SecRandom.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

/// <summary>
///     Version-usage reporting is one best-effort request per start: it carries the build version plus the device
///     UUID (never an account id), it is not gated by the online-status privacy switch, and it must not run on the
///     thread that starts the application.
/// </summary>
public sealed class PlatformVersionReportServiceTests : IDisposable
{
    private const string PlatformId = "69c8cd6a0012dd3ea10a";
    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "SecRandom", "version-report-tests", Guid.NewGuid().ToString("N"));

    public PlatformVersionReportServiceTests()
    {
        ResetDataRootForTests();
        ConfigureDataRootForTests(_dataRoot);
    }

    public void Dispose()
    {
        ResetDataRootForTests();
        if (Directory.Exists(_dataRoot))
            Directory.Delete(_dataRoot, recursive: true);
    }

    [Fact]
    public async Task StartupReportCarriesTheVersionAndTheDeviceUuidOnce()
    {
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var service = CreateService(reports, reported);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            // 只上报一次：没有轮询、也没有身份变化补报
            await Task.Delay(300, TestContext.Current.CancellationToken);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
            service.Dispose();
        }

        JsonElement body = JsonDocument.Parse(Assert.Single(reports)).RootElement;
        Assert.Equal(PlatformId, body.GetProperty("platform_id").GetString());
        Assert.Equal(GlobalConstants.Version, body.GetProperty("version").GetString());
        Assert.True(Guid.TryParse(body.GetProperty("device_uuid").GetString(), out _));
        // 账号 ID 不上报
        Assert.False(body.TryGetProperty("user_id", out _));
    }

    [Fact]
    public async Task OnlineStatusOffStillReportsTheVersion()
    {
        // 版本人数是项目自身的发布基线，刻意不随 OnlineStatusMode 开关关闭
        var config = new MainConfigModel();
        config.General.PrivacySettings.OnlineStatusMode = OnlineStatusMode.Off;
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var service = CreateService(reports, reported, config);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
            service.Dispose();
        }

        JsonElement body = JsonDocument.Parse(Assert.Single(reports)).RootElement;
        Assert.Equal(GlobalConstants.Version, body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task UnreachableEndpointIsSwallowed()
    {
        var reports = new ConcurrentQueue<string>();
        var attempted = new TaskCompletionSource();
        var client = new HttpClient(new StubHttpMessageHandler(_ =>
        {
            attempted.TrySetResult();
            throw new HttpRequestException("offline");
        }));
        var service = CreateService(reports, reported: null, config: null, client);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
            service.Dispose();
        }

        // 统计失败只记日志：不抛异常、不重试、不影响调用方
        Assert.Empty(reports);
    }

    private static PlatformVersionReportService CreateService(
        ConcurrentQueue<string> reports,
        TaskCompletionSource? reported,
        MainConfigModel? config = null,
        HttpClient? httpClient = null)
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config ?? new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var client = httpClient ?? new HttpClient(new StubHttpMessageHandler(request =>
        {
            reports.Enqueue(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            reported?.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        return new PlatformVersionReportService(
            deviceUuidStore,
            new StubHttpClientFactory(client),
            NullLogger<PlatformVersionReportService>.Instance);
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() =>
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($@"Utils.{name} was not found.");

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(send(request));
    }

    private sealed class TestConfigService(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
