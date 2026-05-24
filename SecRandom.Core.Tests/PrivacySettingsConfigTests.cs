namespace SecRandom.Core.Tests;

using OnlineStatusPayload = global::SecRandom.Services.OnlineStatusService.OnlineStatusPayload;
using OnlineStatusPolicy = global::SecRandom.Services.OnlineStatusService.OnlineStatusPolicy;
using IpLocationCache = global::SecRandom.Services.OnlineStatusService.IpLocationCache;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;
using OnlineStatusMode = global::SecRandom.Core.Enums.Configs.OnlineStatusMode;
using PrivacySettingsConfig = global::SecRandom.Core.Models.SubConfigs.General.PrivacySettingsConfig;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using TelemetryMode = global::SecRandom.Core.Enums.Configs.TelemetryMode;
using TelemetryPolicySnapshot = global::SecRandom.Services.Telemetry.TelemetryPolicySnapshot;

public class PrivacySettingsConfigTests
{
    [Fact]
    public void LegacyTelemetryOff_DisablesSentryAndOnlineStatus()
    {
        PrivacySettingsConfig settings = new();

        settings.ApplyLegacyTelemetry(false, TelemetryMode.Full);

        Assert.False(settings.SentryTelemetryEnabled);
        Assert.Equal(OnlineStatusMode.Off, settings.OnlineStatusMode);
    }

    [Fact]
    public void LegacyAnonymousTelemetry_KeepsSentryAndMasksOnlineStatusLocation()
    {
        PrivacySettingsConfig settings = new();

        settings.ApplyLegacyTelemetry(true, TelemetryMode.Anonymous);

        Assert.True(settings.SentryTelemetryEnabled);
        Assert.Equal(OnlineStatusMode.Anonymous, settings.OnlineStatusMode);
    }

    [Fact]
    public void LegacyFullTelemetry_KeepsFullOnlineStatusMode()
    {
        PrivacySettingsConfig settings = new();

        settings.ApplyLegacyTelemetry(true, TelemetryMode.Full);

        Assert.True(settings.SentryTelemetryEnabled);
        Assert.Equal(OnlineStatusMode.Full, settings.OnlineStatusMode);
    }

    [Fact]
    public void LegacyRootBasicJson_MigratesTelemetryIntoGeneralPrivacySettings()
    {
        const string json = """
                            {
                              "basic": {
                                "language": "简体中文",
                                "telemetry_enabled": true,
                                "telemetry_mode": "anonymous"
                              }
                            }
                            """;

        MainConfigModel? settings = System.Text.Json.JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.General.PrivacySettings.SentryTelemetryEnabled);
        Assert.Equal(OnlineStatusMode.Anonymous, settings.General.PrivacySettings.OnlineStatusMode);
    }

    [Fact]
    public void SentryEnabledPolicy_EnablesTracingAndProfilingWithSampling()
    {
        TelemetryPolicySnapshot policy = TelemetryPolicySnapshot.From(true);

        Assert.True(policy.ShouldInitializeSdk);
        Assert.True(policy.ShouldUploadTelemetry);
        Assert.False(policy.SendDefaultPii);
        Assert.True(policy.EnableTraces);
        Assert.True(policy.EnableProfiles);
        Assert.Equal(0.2, policy.TracesSampleRate);
        Assert.Equal(0.2, policy.ProfilesSampleRate);
    }

    [Fact]
    public void OnlineStatusAnonymousPolicy_MasksIpLocationPayload()
    {
        OnlineStatusPolicy policy = OnlineStatusPolicy.From(OnlineStatusMode.Anonymous);
        IpLocationCache cache = new(
            "203.0.113.10",
            "中国",
            "浙江",
            "杭州",
            "西湖",
            DateTimeOffset.UtcNow);

        OnlineStatusPayload payload = OnlineStatusPayload.Create(
            "platform",
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            "windows-desktop",
            policy.IncludeIpLocation ? cache : IpLocationCache.Anonymous);

        Assert.True(policy.IsEnabled);
        Assert.False(policy.IncludeIpLocation);
        Assert.Equal("0.0.0.0", payload.IpAddress);
        Assert.Equal("未知", payload.Country);
        Assert.Equal("未知", payload.Province);
        Assert.Equal("未知", payload.City);
        Assert.Equal("未知", payload.District);
    }

    [Fact]
    public void OnlineStatusOffPolicy_DisablesReporter()
    {
        OnlineStatusPolicy policy = OnlineStatusPolicy.From(OnlineStatusMode.Off);

        Assert.False(policy.IsEnabled);
        Assert.False(policy.IncludeIpLocation);
    }
}
