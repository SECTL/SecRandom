using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Auth;
using SecRandom.Services.Config;

namespace SecRandom.Core.Tests;

public sealed class SectlAuthServiceTests
{
    [Fact]
    public async Task SendHeartbeatAsync_UsesOAuthHeartbeatEndpointAndBearerToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var client = new HttpClient(new StubHttpMessageHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }));
        var service = CreateService(client);
        SetToken(service, new SectlToken("access-token", "refresh-token", "user-1", 3600));

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.True(sent);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://appwrite.sectl.cn/api/oauth/heartbeat", capturedRequest.RequestUri!.ToString());
        Assert.Equal("Bearer access-token", capturedRequest.Headers.Authorization?.ToString());
    }

    [Fact]
    public async Task SendHeartbeatAsync_WhenSignedOut_DoesNotSendARequest()
    {
        var client = new HttpClient(new StubHttpMessageHandler(_ =>
            throw new Xunit.Sdk.XunitException("A signed-out account must not send a heartbeat.")));
        var service = CreateService(client);

        bool sent = await service.SendHeartbeatAsync(TestContext.Current.CancellationToken);

        Assert.False(sent);
    }

    private static SectlAuthService CreateService(HttpClient client)
    {
        var configHandler = new MainConfigHandler(
            NullLogger<MainConfigHandler>.Instance,
            new TestConfigService(new MainConfigModel()));
        var deviceUuidStore = new DeviceUuidStore(configHandler, NullLogger<DeviceUuidStore>.Instance);
        return new SectlAuthService(new StubHttpClientFactory(client), deviceUuidStore);
    }

    private static void SetToken(SectlAuthService service, SectlToken token)
    {
        var field = typeof(SectlAuthService).GetField("_token", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(service, token);
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
