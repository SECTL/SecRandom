using System.Text.Json;
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
                                  "auto_close_time": 8
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
        Assert.Equal(8, settings.NotificationSettings.QuickDraw.AutoCloseTime);
        Assert.False(settings.NotificationSettings.Lottery.Enabled);
        Assert.True(settings.NotificationSettings.Lottery.Animation);
    }

    [Fact]
    public void NotificationSettingsConfig_UsesExpectedDefaults()
    {
        NotificationSettingsConfig settings = new();

        Assert.False(settings.RollCall.Enabled);
        Assert.True(settings.QuickDraw.Enabled);
        Assert.False(settings.Lottery.Enabled);
    }

    [Fact]
    public void MainConfigModel_UsesDefaultNotificationSettingsUntilCategoryIsOverridden()
    {
        MainConfigModel settings = new();
        settings.NotificationSettings.Default.Enabled = true;
        settings.NotificationSettings.Default.AutoCloseTime = 9;
        settings.NotificationSettings.QuickDraw.AutoCloseTime = 3;
        settings.NotificationSettings.QuickDraw.OverrideBasicSettings = false;

        Assert.Same(
            settings.NotificationSettings.Default,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Basic));

        settings.NotificationSettings.QuickDraw.OverrideBasicSettings = true;

        Assert.Same(
            settings.NotificationSettings.QuickDraw,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.QuickDraw,
                OverridableNotificationSettingsType.Basic));
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
        Assert.Same(
            settings.NotificationSettings.RollCall,
            settings.GetOverrideNotificationSettings(
                NotificationSettingsType.RollCall,
                OverridableNotificationSettingsType.Basic));
    }
}
