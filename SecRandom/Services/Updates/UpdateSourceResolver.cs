using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecRandom.Shared.Updates;

namespace SecRandom.Services.Updates;

internal static class SectlSoftwareApi
{
    internal const string BaseUrl = "https://appwrite.sectl.cn";
    internal const string ProjectSlug = "SecRandom";

    internal static Uri LatestTag(UpdateChannel channel) => new(
        $"{BaseUrl}/api/software/latest-tag?projectSlug={ProjectSlug}&channel={ToApiChannel(channel)}");

    internal static Uri UpdateFile(string tag, string fileName) => new(
        $"{BaseUrl}/api/software/update-file?projectSlug={ProjectSlug}"
        + $"&tag={Uri.EscapeDataString(tag)}&fileName={Uri.EscapeDataString(fileName)}");

    internal static string ToApiChannel(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Release => "stable",
        UpdateChannel.Beta => "beta",
        UpdateChannel.Alpha => "alpha",
        _ => "stable"
    };

    // latest-tag 在通道没有领先版本时返回 200 + tag:null，这不是错误，调用方按“暂无版本”处理。
    internal static string? ReadTag(byte[] json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag", out var tag)
            || tag.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tag.GetString()))
            return null;

        return tag.GetString();
    }
}

internal readonly record struct UpdateSourceProbe(UpdateSource Source, TimeSpan Latency, bool Reachable);

/// <summary>
/// 把设置里的更新源偏好展开成有序的具体源：
/// 手动选择 = 所选源优先 + 其余源回退；自动 = 并行探测 TTFB，按延迟升序。
/// 只在这里出现 <see cref="UpdateSource.Auto"/>，下载与验签代码只接受具体源。
/// </summary>
internal sealed class UpdateSourceResolver(HttpClient httpClient)
{
    internal static readonly UpdateSource[] DefaultOrder =
        [UpdateSource.Sectl, UpdateSource.GitHubMirror, UpdateSource.GitHub];

    internal static readonly Uri GitHubRawMetadataUri = new("https://raw.githubusercontent.com/SECTL/SecRandom/master/metadata.yaml");
    internal static readonly Uri GitHubMirrorPrefix = new("https://ghproxy.sectl.cn/");

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);

    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private IReadOnlyList<UpdateSource>? _cachedOrder;
    private DateTimeOffset _cacheExpiresAt;

    internal static Uri MirroredMetadataUri() => new($"{GitHubMirrorPrefix}{GitHubRawMetadataUri.AbsoluteUri}");

    public async Task<IReadOnlyList<UpdateSource>> ResolveAsync(
        UpdateSource preference, bool forceProbe, CancellationToken cancellationToken)
    {
        if (preference != UpdateSource.Auto)
            return [preference, .. DefaultOrder.Where(source => source != preference)];

        if (!forceProbe && TryGetCachedOrder(out var cachedOrder))
            return cachedOrder;

        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceProbe && TryGetCachedOrder(out cachedOrder))
                return cachedOrder;

            var probes = await Task.WhenAll(DefaultOrder.Select(source => ProbeAsync(source, cancellationToken)))
                .ConfigureAwait(false);
            var ordered = Order(probes);
            _cachedOrder = ordered;
            _cacheExpiresAt = DateTimeOffset.UtcNow + CacheDuration;
            return ordered;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    internal static IReadOnlyList<UpdateSource> Order(IEnumerable<UpdateSourceProbe> probes) =>
        probes.OrderBy(probe => probe.Reachable ? probe.Latency : TimeSpan.MaxValue)
            .Select(probe => probe.Source)
            .ToArray();

    internal static Uri ProbeUri(UpdateSource source) => source switch
    {
        UpdateSource.Sectl => SectlSoftwareApi.LatestTag(UpdateChannel.Release),
        UpdateSource.GitHubMirror => MirroredMetadataUri(),
        UpdateSource.GitHub => GitHubRawMetadataUri,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Only concrete update sources can be probed.")
    };

    // 只读响应头就断开：探测只量首字节时间，不下载正文。
    private async Task<UpdateSourceProbe> ProbeAsync(UpdateSource source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProbeUri(source));
            using var response = await httpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            return new UpdateSourceProbe(source, stopwatch.Elapsed, response.IsSuccessStatusCode);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateSourceProbe(source, TimeSpan.MaxValue, false);
        }
    }

    private bool TryGetCachedOrder(out IReadOnlyList<UpdateSource> order)
    {
        if (_cachedOrder is not null && DateTimeOffset.UtcNow < _cacheExpiresAt)
        {
            order = _cachedOrder;
            return true;
        }

        order = DefaultOrder;
        return false;
    }
}