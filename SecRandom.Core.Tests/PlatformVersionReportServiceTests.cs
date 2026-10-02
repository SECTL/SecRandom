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
using SecRandom.Services.Auth;
using SecRandom.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Core.Tests;

/// <summary>
///     Version-usage reporting is a one-shot, identity-deduplicated report: once per start, once more when the
///     signed-in account changes, never a duplicate for an identity the service already credited, and nothing at
///     all while online-status reporting is off.
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
    public async Task StartupReportCarriesTheVersionAndTheDeviceIdentity()
    {
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var service = CreateService(reports, reported);

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
        Assert.Equal(PlatformId, body.GetProperty("platform_id").GetString());
        Assert.Equal(GlobalConstants.Version, body.GetProperty("version").GetString());
        Assert.False(body.TryGetProperty("user_id", out _));
        Assert.True(Guid.TryParse(body.GetProperty("device_uuid").GetString(), out _));
    }

    [Fact]
    public async Task OnlineStatusOffStillReportsTheVersion()
    {
        // 版本人数是项目自身的发布基线，刻意不随 OnlineStatusMode 开关关闭
        var config = new MainConfigModel();
        config.General.PrivacySettings.OnlineStatusMode = OnlineStatusMode.Off;
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var service = CreateService(reports, reported, config: config);

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
    public async Task SignInReportsAgainWithTheAccountIdentity()
    {
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var identity = new StubIdentitySource();
        var service = CreateService(reports, reported, identity);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            identity.SetUserId("user-1");
            await WaitUntilAsync(() => reports.Count >= 2, TimeSpan.FromSeconds(5));
        }
        finally
        {
            await service.StopAsync(TestContext.Current.CancellationToken);
            service.Dispose();
        }

        string[] bodies = reports.ToArray();
        Assert.Equal(2, bodies.Length);
        JsonElement startup = JsonDocument.Parse(bodies[0]).RootElement;
        JsonElement signedIn = JsonDocument.Parse(bodies[1]).RootElement;
        Assert.False(startup.TryGetProperty("user_id", out _));
        Assert.Equal("user-1", signedIn.GetProperty("user_id").GetString());
        Assert.False(signedIn.TryGetProperty("device_uuid", out _));
    }

    [Fact]
    public async Task RepeatedIdentityNotificationDoesNotReportTheSameIdentityTwice()
    {
        var reports = new ConcurrentQueue<string>();
        var reported = new TaskCompletionSource();
        var identity = new StubIdentitySource();
        var service = CreateService(reports, reported, identity);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await reported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        identity.RaiseChanged();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        service.Dispose();

        Assert.Single(reports);
    }

    [Fact]
    public void AuthIdentitySourceForwardsOnlyRealAccountChanges()
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var authService = new SectlAuthService(
            new StubHttpClientFactory(new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)))),
            deviceUuidStore);
        SetToken(authService, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        using var source = new SectlStatsAccountIdentitySource(authService);
        var changes = 0;
        source.UserIdChanged += (_, _) => changes++;
        Assert.Equal("user-1", source.UserId);

        // 账号资料刷新也会触发 StateChanged，身份没变就不该再上报一次
        RaiseStateChanged(authService);
        Assert.Equal(0, changes);

        SetToken(authService, null);
        RaiseStateChanged(authService);
        Assert.Equal(1, changes);
        Assert.Null(source.UserId);
    }

    private static PlatformVersionReportService CreateService(
        ConcurrentQueue<string> reports,
        TaskCompletionSource? reported,
        IStatsAccountIdentitySource? identitySource = null,
        MainConfigModel? config = null)
    {
        // 版本上报自身不再读取隐私设置；这里仍按传入配置建 Host，用来断言开关关闭时也照常上报
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(config ?? new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            reports.Enqueue(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            reported?.TrySetResult();
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        return new PlatformVersionReportService(
            deviceUuidStore,
            identitySource ?? new StubIdentitySource(),
            new StubHttpClientFactory(client),
            NullLogger<PlatformVersionReportService>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(25, TestContext.Current.CancellationToken);
    }

    private static void SetToken(SectlAuthService service, SectlToken? token)
    {
        var field = typeof(SectlAuthService).GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, token);
    }

    private static void RaiseStateChanged(SectlAuthService service)
    {
        var field = typeof(SectlAuthService).GetField("StateChanged", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        ((EventHandler?)field!.GetValue(service))?.Invoke(service, EventArgs.Empty);
    }

    private static void ConfigureDataRootForTests(string dataRoot) =>
        GetUtilsMethod("ConfigureDataRoot").Invoke(null, [dataRoot]);

    private static void ResetDataRootForTests() =>
        GetUtilsMethod("ResetDataRootForTests").Invoke(null, null);

    private static MethodInfo GetUtilsMethod(string name) =>
        typeof(Utils).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($@"Utils.{name} was not found.");

    private sealed class StubIdentitySource : IStatsAccountIdentitySource
    {
        private string? _userId;

        public string? UserId => _userId;

        public event EventHandler? UserIdChanged;

        public void SetUserId(string? userId)
        {
            if (string.Equals(_userId, userId, StringComparison.Ordinal))
                return;

            _userId = userId;
            RaiseChanged();
        }

        public void RaiseChanged() => UserIdChanged?.Invoke(this, EventArgs.Empty);
    }

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
