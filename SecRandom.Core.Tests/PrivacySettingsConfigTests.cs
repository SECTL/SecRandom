namespace SecRandom.Core.Tests;

using OnlineStatusPayload = global::SecRandom.Services.OnlineStatusService.OnlineStatusPayload;
using OnlineStatusPolicy = global::SecRandom.Services.OnlineStatusService.OnlineStatusPolicy;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;
using OnlineStatusMode = global::SecRandom.Core.Enums.Configs.OnlineStatusMode;
using PrivacySettingsConfig = global::SecRandom.Core.Models.SubConfigs.General.PrivacySettingsConfig;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using GlobalConstants = global::SecRandom.Core.GlobalConstants;
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
        double expectedSampleRate = GlobalConstants.IsDevelopment ? 1.0 : 0.2;
        Assert.Equal(expectedSampleRate, policy.TracesSampleRate);
        Assert.Equal(expectedSampleRate, policy.ProfilesSampleRate);
    }

    [Fact]
    public void OnlineStatusPayloadCarriesNoAddressOrRegion()
    {
        OnlineStatusPolicy policy = OnlineStatusPolicy.From(OnlineStatusMode.Anonymous);
        OnlineStatusPayload payload = OnlineStatusPayload.Create(
            "platform",
            Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            "windows-desktop");

        Assert.True(policy.IsEnabled);
        Assert.Equal("platform", payload.PlatformId);
        Assert.Equal("01234567-89ab-cdef-0123-456789abcdef", payload.DeviceUuid);
        Assert.Equal("windows-desktop", payload.DeviceType);

        // The service derives the address and its region from the connection, so the client must not
        // ship either — and both reporting modes now send exactly the same fields.
        string serialized = System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        foreach (string field in new[] { "ip_address", "country", "province", "city", "district" })
            Assert.DoesNotContain(field, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlineStatusOffPolicy_DisablesReporter()
    {
        OnlineStatusPolicy policy = OnlineStatusPolicy.From(OnlineStatusMode.Off);

        Assert.False(policy.IsEnabled);
    }
}
