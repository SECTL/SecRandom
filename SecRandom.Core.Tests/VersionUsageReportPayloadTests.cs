using System.Text.Json;
using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The version-usage counter answers "how many people are on this build": the service dedups by identity and
///     keeps one current version per identity. This client sends only the pseudo-anonymous device UUID — never an
///     account id — and the payload has to keep that shape with the API's own field names.
/// </summary>
public sealed class VersionUsageReportPayloadTests
{
    private const string DeviceUuid = "01234567-89AB-CDEF-0123-456789ABCDEF";
    private const string LowerDeviceUuid = "01234567-89ab-cdef-0123-456789abcdef";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PayloadCarriesTheVersionAndTheDeviceUuidOnly()
    {
        var payload = VersionUsageReportPayload.Create("pf_abc", "v3.0.0-alpha.2", $" {DeviceUuid} ");

        Assert.Equal("pf_abc", payload.PlatformId);
        Assert.Equal("v3.0.0-alpha.2", payload.Version);
        // 与在线状态上报同为小写形态，同一台设备只对应一个身份字符串
        Assert.Equal(LowerDeviceUuid, payload.DeviceUuid);

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        Assert.Contains("\"platform_id\":\"pf_abc\"", json);
        Assert.Contains("\"version\":\"v3.0.0-alpha.2\"", json);
        Assert.Contains($"\"device_uuid\":\"{LowerDeviceUuid}\"", json);
        Assert.DoesNotContain("platformId", json);
        Assert.DoesNotContain("deviceUuid", json);
        // 账号 ID 永远不上报，版本人数不能与 SECTL 账号关联
        Assert.DoesNotContain("user_id", json);
        Assert.DoesNotContain("userId", json);
    }

    [Fact]
    public void ReportWithoutAUsableDeviceUuidIsRejected()
    {
        Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", "1.8.0", null));
        Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", "1.8.0", "   "));
        Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", "1.8.0", "not-a-uuid"));
    }

    [Theory]
    [InlineData("1.8.0", true)]
    [InlineData("v2.1.0-beta.1+build.3", true)]
    [InlineData("2026.05", true)]
    [InlineData("Windows 1.8.0", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(".8.0", false)]
    [InlineData("1.8.0;drop_table", false)]
    [InlineData("1.8/0", false)]
    public void VersionRuleMatchesTheApi(string version, bool supported)
    {
        Assert.Equal(supported, VersionUsageReportPayload.IsSupportedVersion(version));
        if (!supported)
            Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", version, DeviceUuid));
    }

    [Fact]
    public void VersionLengthIsBoundedToOneToSixtyFourCharacters()
    {
        Assert.True(VersionUsageReportPayload.IsSupportedVersion(new string('1', 64)));
        Assert.False(VersionUsageReportPayload.IsSupportedVersion(new string('1', 65)));
    }
}
