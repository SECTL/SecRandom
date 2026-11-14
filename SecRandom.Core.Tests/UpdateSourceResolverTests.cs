using System.Net.Http;
using System.Text;
using SecRandom.Services.Updates;
using SecRandom.Shared.Updates;

namespace SecRandom.Core.Tests;

public sealed class UpdateSourceResolverTests
{
    [Fact]
    public void Order_puts_fastest_reachable_source_first_and_unreachable_last()
    {
        var order = UpdateSourceResolver.Order(
        [
            new UpdateSourceProbe(UpdateSource.Sectl, TimeSpan.MaxValue, false),
            new UpdateSourceProbe(UpdateSource.GitHubMirror, TimeSpan.FromMilliseconds(120), true),
            new UpdateSourceProbe(UpdateSource.GitHub, TimeSpan.FromMilliseconds(30), true)
        ]);

        Assert.Equal(new[] { UpdateSource.GitHub, UpdateSource.GitHubMirror, UpdateSource.Sectl }, order);
    }

    [Fact]
    public void Order_keeps_default_priority_when_every_source_is_unreachable()
    {
        // ResolveAsync 按 DefaultOrder 发起探测，排序对相同延迟保持稳定，
        // 所以全部不可达时仍然回落到默认优先级。
        var order = UpdateSourceResolver.Order(
        [
            new UpdateSourceProbe(UpdateSource.Sectl, TimeSpan.MaxValue, false),
            new UpdateSourceProbe(UpdateSource.GitHubMirror, TimeSpan.MaxValue, false),
            new UpdateSourceProbe(UpdateSource.GitHub, TimeSpan.MaxValue, false)
        ]);

        Assert.Equal(UpdateSourceResolver.DefaultOrder, order);
    }

    [Fact]
    public async Task Manual_choice_is_tried_first_and_keeps_a_fallback_chain()
    {
        using var httpClient = new HttpClient();
        var resolver = new UpdateSourceResolver(httpClient);

        var order = await resolver.ResolveAsync(UpdateSource.GitHubMirror, false, CancellationToken.None);

        Assert.Equal(
            new[] { UpdateSource.GitHubMirror, UpdateSource.Sectl, UpdateSource.GitHub },
            order);
    }

    [Fact]
    public async Task Auto_falls_back_to_default_priority_when_no_probe_succeeds()
    {
        using var httpClient = new HttpClient(new FailingHandler());
        var resolver = new UpdateSourceResolver(httpClient);

        var order = await resolver.ResolveAsync(UpdateSource.Auto, true, CancellationToken.None);

        Assert.Equal(UpdateSourceResolver.DefaultOrder, order);
    }

    [Theory]
    [InlineData("{\"tag\":\"v3.1.5\"}", "v3.1.5")]
    [InlineData("{\"tag\":null}", null)]
    [InlineData("{}", null)]
    [InlineData("{\"tag\":\"   \"}", null)]
    public void ReadTag_treats_an_empty_channel_as_no_version(string json, string? expected)
    {
        Assert.Equal(expected, SectlSoftwareApi.ReadTag(Encoding.UTF8.GetBytes(json)));
    }

    [Theory]
    [InlineData(UpdateChannel.Release, "stable")]
    [InlineData(UpdateChannel.Beta, "beta")]
    [InlineData(UpdateChannel.Alpha, "alpha")]
    public void Api_channel_names_match_the_platform_api(UpdateChannel channel, string expected)
    {
        Assert.Equal(expected, SectlSoftwareApi.ToApiChannel(channel));
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("offline");
    }
}