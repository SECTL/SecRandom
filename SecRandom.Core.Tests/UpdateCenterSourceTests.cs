using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Config;
using SecRandom.Services.Updates;
using SecRandom.Shared.Updates;

namespace SecRandom.Core.Tests;

public sealed class UpdateCenterSourceTests
{
    [Fact]
    public async Task Check_ContinuesAfterSourceTimeout()
    {
        var requests = new List<Uri>();
        using var client = new HttpClient(new Handler(request =>
        {
            requests.Add(request.RequestUri!);
            if (requests.Count == 1)
                throw new TaskCanceledException("source timed out");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
        var service = CreateService(client);

        await service.CheckAsync();

        Assert.Equal(UpdateOperationPhase.CheckFailed, service.Phase);
        Assert.Equal(3, requests.Count);
        Assert.Equal("appwrite.sectl.cn", requests[0].Host);
        Assert.Equal("ghproxy.sectl.cn", requests[1].Host);
        Assert.Equal("raw.githubusercontent.com", requests[2].Host);
    }

    [Fact]
    public async Task Download_RetriesAnotherSourceAfterHashMismatch()
    {
        var expected = Encoding.UTF8.GetBytes("valid package");
        var attempts = 0;
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(++attempts == 1 ? Encoding.UTF8.GetBytes("invalid data!") : expected)
        }));
        var artifact = new UpdateArtifact
        {
            AssetName = "package.zip", ByteLength = expected.Length, Sha512 = Convert.ToHexString(SHA512.HashData(expected))
        };

        var result = await CreateService(client).DownloadAssetWithFallbackAsync("v3.2.0", artifact, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void RuntimeDetection_UsesCoreRuntimeRequiredByAvalonia()
    {
        Assert.True(UpdateCenterService.ContainsRequiredRuntime("Microsoft.NETCore.App 10.0.1 [C:\\dotnet]\n"));
        Assert.False(UpdateCenterService.ContainsRequiredRuntime("Microsoft.WindowsDesktop.App 10.0.1 [C:\\dotnet]\n"));
    }

    private static UpdateCenterService CreateService(HttpClient client)
    {
        var config = new MainConfigModel();
        config.UpdateSettings.UpdateSource = UpdateSource.Sectl;
        return new UpdateCenterService(
            new MainConfigHandler(NullLogger<MainConfigHandler>.Instance, new MemoryConfig(config)),
            NullLogger<UpdateCenterService>.Instance, client);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class MemoryConfig(MainConfigModel config) : ConfigServiceBase
    {
        public override bool IsConfigExists<T>(T fallback) => true;
        public override T LoadConfig<T>(T fallback) => config is T typed ? typed : fallback;
        public override void SaveConfig<T>(T value) { }
        public override void DeleteConfig<T>(T value) { }
    }
}
