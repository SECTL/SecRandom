using System.Text.Json;
using SecRandom.Core.Services.Stats;
using SecRandom.Services;

namespace SecRandom.Core.Tests;

/// <summary>
///     Every reporting channel posts to the one aggregate entrance instead of a per-type endpoint, and its payload
///     is judged as the report type it is meant to be. The judgment rule is the service's, reproduced here in the
///     order the API documents it, because the failure mode of a mis-typed payload is silent: posting the right
///     fields to the wrong old endpoint — or adding a field of an earlier rule — does not fail, it books the number
///     under another figure.
/// </summary>
public sealed class PlatformStatsReportEntranceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void EveryChannelUsesTheAggregateEntrance()
    {
        Assert.Equal("https://appwrite.sectl.cn/api/stats/report", PlatformStatsApi.ReportUri.ToString());
        // 四个旧端点只是兼容保留，客户端不再调用
        foreach (var legacy in new[]
                 {
                     "/api/stats/online",
                     "/api/stats/usage/increment",
                     "/api/stats/version",
                     "/api/fields/values"
                 })
            Assert.DoesNotContain(legacy, PlatformStatsApi.ReportUri.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineHeartbeatIsJudgedAsAnOnlineReport()
    {
        var payload = OnlineStatusService.OnlineStatusPayload.Create(
            PlatformStatsApi.PlatformId,
            Guid.NewGuid(),
            "windows-desktop");

        // 心跳载荷只有 platform_id / device_uuid / device_type，落到兜底判定
        Assert.Equal("online", Judge(Serialize(payload)));
    }

    [Fact]
    public void UsageIncrementIsJudgedAsAUsageIncrement()
    {
        // field_key 没有配套的 value，且带 delta，才是累计增量
        var payload = new UsageIncrementPayload(PlatformStatsApi.PlatformId, "daily_roll_call_count", 2);
        Assert.Equal("usage", Judge(Serialize(payload)));
    }

    [Fact]
    public void VersionReportIsJudgedAsAVersionReport()
    {
        var payload = VersionUsageReportPayload.Create(
            PlatformStatsApi.PlatformId,
            "v3.0.0-alpha.2",
            Guid.NewGuid().ToString("D"));

        Assert.Equal("version", Judge(Serialize(payload)));
    }

    private static JsonElement Serialize<T>(T payload) =>
        JsonSerializer.SerializeToElement(payload, JsonOptions);

    /// <summary>
    ///     The API's type judgment: first hit wins, keys only — value validity is each handler's own business.
    /// </summary>
    private static string Judge(JsonElement body)
    {
        var keys = body.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);

        if (keys.Contains("field_key") && keys.Contains("value"))
            return "field";
        if (keys.Contains("version") || keys.Contains("app_version"))
            return "version";
        if (new[] { "metrics", "increments", "usage", "delta", "metric_key" }.Any(keys.Contains))
            return "usage";
        if (keys.Contains("device_uuid") || keys.Contains("platform_id"))
            return "online";

        return "undetermined";
    }
}
