using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;

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
}
