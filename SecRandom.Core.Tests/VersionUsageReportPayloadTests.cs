using System.Text.Json;
using SecRandom.Core.Services.Stats;

namespace SecRandom.Core.Tests;

/// <summary>
///     The version-usage counter answers "how many people are on this build": the service dedups by identity and
///     keeps one current version per identity, so the payload has to carry exactly one identity and the API's own
///     field names — camel-casing them or sending both identities would silently corrupt the figure.
/// </summary>
public sealed class VersionUsageReportPayloadTests
{
    private const string DeviceUuid = "01234567-89AB-CDEF-0123-456789ABCDEF";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SignedInAccountWinsAndTheDeviceIdentityIsOmitted()
    {
        var payload = VersionUsageReportPayload.Create("pf_abc", "1.8.0", " user-1 ", DeviceUuid);

        Assert.Equal("user-1", payload.UserId);
        Assert.Null(payload.DeviceUuid);
        Assert.Equal("user-1", payload.Identity);

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        Assert.Contains("\"user_id\":\"user-1\"", json);
        Assert.DoesNotContain("device_uuid", json);
    }

    [Fact]
    public void SignedOutInstallationReportsTheDeviceUuid()
    {
        var payload = VersionUsageReportPayload.Create("pf_abc", "1.8.0", "   ", DeviceUuid);

        Assert.Null(payload.UserId);
        // 与在线状态上报同为小写形态，同一台设备只对应一个身份字符串
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", payload.DeviceUuid);
        Assert.Equal(payload.DeviceUuid, payload.Identity);

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        Assert.Contains("\"device_uuid\":\"01234567-89ab-cdef-0123-456789abcdef\"", json);
        Assert.DoesNotContain("user_id", json);
    }

    [Fact]
    public void PayloadKeepsTheApiFieldNames()
    {
        string json = JsonSerializer.Serialize(
            VersionUsageReportPayload.Create("pf_abc", "v3.0.0-alpha.2", null, DeviceUuid),
            JsonOptions);

        Assert.Contains("\"platform_id\":\"pf_abc\"", json);
        Assert.Contains("\"version\":\"v3.0.0-alpha.2\"", json);
        Assert.DoesNotContain("platformId", json);
        Assert.DoesNotContain("userId", json);
        Assert.DoesNotContain("identity", json);
    }

    [Fact]
    public void ReportWithoutAnyIdentityIsRejected()
    {
        Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", "1.8.0", null, "  "));
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
            Assert.Throws<ArgumentException>(() => VersionUsageReportPayload.Create("pf_abc", version, "user-1", null));
    }

    [Fact]
    public void VersionLengthIsBoundedToOneToSixtyFourCharacters()
    {
        Assert.True(VersionUsageReportPayload.IsSupportedVersion(new string('1', 64)));
        Assert.False(VersionUsageReportPayload.IsSupportedVersion(new string('1', 65)));
    }
}
