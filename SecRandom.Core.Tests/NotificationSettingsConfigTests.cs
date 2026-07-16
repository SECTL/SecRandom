using System.Text.Json;
using System.Xml.Linq;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Tests;

public class NotificationSettingsConfigTests
{
    [Fact]
    public void MainConfigModel_DeserializesNotificationSettings()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "roll_call": {
                                  "enabled": true
                                },
                               "quick_draw": {
                                  "enabled": false,
                                  "display_duration": 8
                                },
                                "lottery": {}
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.NotificationSettings.RollCall.Enabled);
        Assert.False(settings.NotificationSettings.QuickDraw.Enabled);
        Assert.Equal(8, settings.NotificationSettings.QuickDraw.DisplayDuration);
        Assert.False(settings.NotificationSettings.Lottery.Enabled);
        Assert.True(settings.NotificationSettings.Lottery.Animation);
    }

    [Fact]
    public void NotificationSettingsConfig_UsesExpectedDefaults()
    {
        NotificationSettingsConfig settings = new();

        Assert.False(settings.RollCall.Enabled);
        Assert.True(settings.QuickDraw.Enabled);
        Assert.Equal(3, settings.Default.DisplayDuration);
        Assert.Equal(3, settings.RollCall.DisplayDuration);
        Assert.Equal(3, settings.QuickDraw.DisplayDuration);
        Assert.Equal(3, settings.Lottery.DisplayDuration);
        Assert.False(settings.Lottery.Enabled);
        Assert.False(settings.RollCall.OverrideBasicSettings);
        Assert.False(settings.RollCall.OverrideNotificationWindowSettings);
        Assert.False(settings.RollCall.OverrideServiceSettings);
        Assert.True(settings.QuickDraw.OverrideBasicSettings);
        Assert.False(settings.QuickDraw.OverrideNotificationWindowSettings);
        Assert.False(settings.QuickDraw.OverrideServiceSettings);
        Assert.False(settings.Lottery.OverrideBasicSettings);
        Assert.False(settings.Lottery.OverrideNotificationWindowSettings);
        Assert.False(settings.Lottery.OverrideServiceSettings);
    }

    [Fact]
    public void MainConfigModel_QuickDrawNotificationCanBeDisabledIndependently()
    {
        MainConfigModel settings = new();
        settings.NotificationSettings.QuickDraw.Enabled = false;

        Assert.False(settings.GetOverrideNotificationSettings(
            NotificationSettingsType.QuickDraw,
            OverridableNotificationSettingsType.Basic).Enabled);
        Assert.False(settings.NotificationSettings.RollCall.Enabled);
        Assert.False(settings.NotificationSettings.Lottery.Enabled);
    }

    [Fact]
    public void NotificationChannelSettings_UsesLanguageNeutralMonitorDefault()
    {
        NotificationSettingsConfig settings = new();

        Assert.Empty(settings.Default.EnabledMonitor);
        Assert.Empty(settings.RollCall.EnabledMonitor);
        Assert.Empty(settings.QuickDraw.EnabledMonitor);
        Assert.Empty(settings.Lottery.EnabledMonitor);
    }

    [Theory]
    [InlineData("\"OFF\"")]
    [InlineData("\"off\"")]
    [InlineData("null")]
    public void NotificationChannelSettings_LegacyOrNullMonitorUsesUnspecified(string jsonValue)
    {
        NotificationChannelSettings? settings = JsonSerializer.Deserialize<NotificationChannelSettings>(
            $$"""{"enabled_monitor":{{jsonValue}}}""",
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.Empty(settings.EnabledMonitor);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void NotificationChannelSettings_InvalidServiceTypeUsesBuiltIn(int value)
    {
        NotificationChannelSettings settings = new() { NotificationServiceType = value };

        Assert.Equal(0, settings.NotificationServiceType);
    }

    [Fact]
    public void MainConfigModel_NullNotificationSettingsUsesDefaults()
    {
        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            """{"notification_settings":null}""",
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.NotNull(settings.NotificationSettings);
        Assert.True(settings.NotificationSettings.QuickDraw.Enabled);
    }

    [Fact]
    public void NotificationSettingsConfig_NullChannelsUseDefaults()
    {
        NotificationSettingsConfig? settings = JsonSerializer.Deserialize<NotificationSettingsConfig>(
            """{"default":null,"roll_call":null,"quick_draw":null,"lottery":null}""",
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.NotNull(settings.Default);
        Assert.NotNull(settings.RollCall);
        Assert.NotNull(settings.QuickDraw);
        Assert.NotNull(settings.Lottery);
        Assert.True(settings.QuickDraw.Enabled);
    }

    [Theory]
    [InlineData("Resources.resx", "不指定")]
    [InlineData("Resources.en-US.resx", "Not specified")]
    [InlineData("Resources.ja-JP.resx", "指定しない")]
    public void NotificationResources_LocalizeUnspecifiedMonitor(string fileName, string expected)
    {
        string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        string resourcePath = Path.Combine(
            repositoryRoot,
            "SecRandom/Langs/SettingsPages/Notification",
            fileName);
        XDocument resource = XDocument.Load(resourcePath);
        string? actual = resource.Root?
            .Elements("data")
            .SingleOrDefault(element => (string?)element.Attribute("name") == "O_Monitor_Unspecified")?
            .Element("value")?
            .Value;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void MainConfigModel_UsesChannelNotificationBasicSettings()
    {
        MainConfigModel settings = new();
        settings.NotificationSettings.Default.Enabled = true;
        settings.NotificationSettings.QuickDraw.OverrideBasicSettings = false;

        Assert.Same(
            settings.NotificationSettings.QuickDraw,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Basic));
    }

    [Fact]
    public void MainConfigModel_UsesPerChannelNotificationServiceOverride()
    {
        MainConfigModel settings = new();
        settings.NotificationSettings.Default.NotificationServiceType = 1;
        settings.NotificationSettings.QuickDraw.NotificationServiceType = 2;

        Assert.Same(
            settings.NotificationSettings.Default,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Service));

        settings.NotificationSettings.QuickDraw.OverrideServiceSettings = true;

        Assert.Same(
            settings.NotificationSettings.QuickDraw,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Service));
        Assert.Equal(2, settings.GetOverrideNotificationSettings(
            NotificationSettingsType.QuickDraw,
            OverridableNotificationSettingsType.Service).NotificationServiceType);
    }

    [Fact]
    public void MainConfigModel_PreservesLegacyNotificationSettingsAsOverrides()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "roll_call": {
                                  "enabled": true
                                }
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.NotificationSettings.RollCall.OverrideBasicSettings);
        Assert.True(settings.NotificationSettings.RollCall.OverrideNotificationWindowSettings);
        Assert.True(settings.NotificationSettings.RollCall.OverrideServiceSettings);
        Assert.True(settings.NotificationSettings.QuickDraw.OverrideBasicSettings);
        Assert.True(settings.NotificationSettings.QuickDraw.OverrideNotificationWindowSettings);
        Assert.True(settings.NotificationSettings.QuickDraw.OverrideServiceSettings);
        Assert.True(settings.NotificationSettings.Lottery.OverrideBasicSettings);
        Assert.True(settings.NotificationSettings.Lottery.OverrideNotificationWindowSettings);
        Assert.True(settings.NotificationSettings.Lottery.OverrideServiceSettings);
        Assert.Same(
            settings.NotificationSettings.RollCall,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.RollCall,
                OverridableNotificationSettingsType.Basic));
    }

    [Fact]
    public void MainConfigModel_MigratesGlobalNotificationServiceWithoutLosingChannelEnabledStates()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "default": { "notification_service_type": 1 },
                                "roll_call": { "enabled": true },
                                "quick_draw": { "enabled": false }
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.NotificationSettings.RollCall.OverrideBasicSettings);
        Assert.True(settings.NotificationSettings.QuickDraw.OverrideBasicSettings);
        Assert.False(settings.NotificationSettings.RollCall.OverrideServiceSettings);
        Assert.False(settings.NotificationSettings.QuickDraw.OverrideServiceSettings);
        Assert.True(settings.GetOverrideNotificationSettings(
            NotificationSettingsType.RollCall,
            OverridableNotificationSettingsType.Basic).Enabled);
        Assert.False(settings.GetOverrideNotificationSettings(
            NotificationSettingsType.QuickDraw,
            OverridableNotificationSettingsType.Basic).Enabled);
        Assert.Equal(1, settings.GetOverrideNotificationSettings(
            NotificationSettingsType.RollCall,
            OverridableNotificationSettingsType.Service).NotificationServiceType);
    }

    [Fact]
    public void MainConfigModel_MigratesInheritedEnabledAndAnimationSettingsToEachChannel()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "override_defaults_initialized": true,
                                "default": {
                                  "enabled": true,
                                  "animation": false
                                },
                                "roll_call": {
                                  "override_basic_settings": false
                                }
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.NotificationSettings.RollCall.Enabled);
        Assert.False(settings.NotificationSettings.RollCall.Animation);
        Assert.True(settings.NotificationSettings.RollCall.OverrideBasicSettings);
        Assert.True(settings.NotificationSettings.BasicSettingsSeparatedInitialized);
    }

    [Fact]
    public void MainConfigModel_DraftNotificationSchemaResetsEnabledOverridesOnce()
    {
        const string json = """
                            {
                              "notification_settings": {
                                "default": {},
                                "roll_call": { "override_basic_settings": true },
                                "quick_draw": { "override_notification_window_settings": true },
                                "lottery": { "override_service_settings": true }
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.True(settings.NotificationSettings.RollCall.OverrideBasicSettings);
        Assert.False(settings.NotificationSettings.QuickDraw.OverrideNotificationWindowSettings);
        Assert.False(settings.NotificationSettings.Lottery.OverrideServiceSettings);
    }

    [Fact]
    public void MainConfigModel_NotificationSettingsRoundTripPersistsAllCategories()
    {
        MainConfigModel settings = new();
        settings.NotificationSettings.Default.Enabled = true;
        settings.NotificationSettings.Default.EnabledMonitor = "Display 1";
        settings.NotificationSettings.RollCall.OverrideBasicSettings = true;
        settings.NotificationSettings.RollCall.OverrideServiceSettings = true;
        settings.NotificationSettings.RollCall.DisplayDuration = 11;
        settings.NotificationSettings.QuickDraw.OverrideNotificationWindowSettings = true;
        settings.NotificationSettings.QuickDraw.HorizontalOffset = 42;
        settings.NotificationSettings.Lottery.OverrideServiceSettings = true;
        settings.NotificationSettings.Lottery.NotificationServiceType = 2;

        string json = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.True(restored.NotificationSettings.Default.Enabled);
        Assert.Equal("Display 1", restored.NotificationSettings.Default.EnabledMonitor);
        Assert.True(restored.NotificationSettings.RollCall.OverrideBasicSettings);
        Assert.True(restored.NotificationSettings.RollCall.OverrideServiceSettings);
        Assert.Equal(11, restored.NotificationSettings.RollCall.DisplayDuration);
        Assert.True(restored.NotificationSettings.QuickDraw.OverrideNotificationWindowSettings);
        Assert.Equal(42, restored.NotificationSettings.QuickDraw.HorizontalOffset);
        Assert.True(restored.NotificationSettings.Lottery.OverrideServiceSettings);
        Assert.Equal(2, restored.NotificationSettings.Lottery.NotificationServiceType);
    }

    [Fact]
    public void NotificationChannelSettings_MigratesClassIslandFallbackSettingToServiceFallback()
    {
        const string json = """
                            { "use_built_in_on_class_island_failure": false }
                            """;

        NotificationChannelSettings? settings = JsonSerializer.Deserialize<NotificationChannelSettings>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.False(settings.UseBuiltInOnServiceFailure);

        string serialized = JsonSerializer.Serialize(settings, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"use_built_in_on_service_failure\": false", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("use_built_in_on_class_island_failure", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "{ \"display_duration\": 12, \"auto_close_time\": 60 }",
        "{ \"auto_close_time\": 60, \"display_duration\": 12 }")]
    public void NotificationChannelSettings_MigratesLegacyAutoCloseTimeWithoutOverridingDisplayDuration(
        string displayDurationFirst,
        string legacyAutoCloseTimeFirst)
    {
        foreach (var json in new[] { displayDurationFirst, legacyAutoCloseTimeFirst })
        {
            NotificationChannelSettings? settings = JsonSerializer.Deserialize<NotificationChannelSettings>(
                json,
                ConfigServiceBase.JsonOptions);

            Assert.NotNull(settings);
            Assert.Equal(12, settings.DisplayDuration);
        }

        NotificationChannelSettings? migrated = JsonSerializer.Deserialize<NotificationChannelSettings>(
            "{ \"auto_close_time\": 60 }",
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(migrated);
        Assert.Equal(60, migrated.DisplayDuration);

        string serialized = JsonSerializer.Serialize(migrated, ConfigServiceBase.JsonOptions);
        Assert.Contains("\"display_duration\": 60", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("auto_close_time", serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "{ \"use_built_in_on_service_failure\": true, \"use_built_in_on_class_island_failure\": false }",
        "{ \"use_built_in_on_class_island_failure\": false, \"use_built_in_on_service_failure\": true }")]
    public void NotificationChannelSettings_PrefersServiceFallbackSettingOverLegacySetting(
        string newSettingFirst,
        string legacySettingFirst)
    {
        foreach (var json in new[] { newSettingFirst, legacySettingFirst })
        {
            NotificationChannelSettings? settings = JsonSerializer.Deserialize<NotificationChannelSettings>(
                json,
                ConfigServiceBase.JsonOptions);

            Assert.NotNull(settings);
            Assert.True(settings.UseBuiltInOnServiceFailure);
        }
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, false, true)]
    [InlineData(2, true, true)]
    public void NotificationChannelSettings_ExposesUnifiedServiceSelection(
        int serviceType,
        bool usesBuiltIn,
        bool usesExternal)
    {
        NotificationChannelSettings settings = new() { NotificationServiceType = serviceType };

        Assert.Equal(usesBuiltIn, settings.UsesBuiltInNotificationService);
        Assert.Equal(usesExternal, settings.UsesExternalNotificationService);
    }
}
