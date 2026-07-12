using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs.Picking;

namespace SecRandom.Core.Tests;

public class QuickDrawSettingsConfigTests
{
    [Fact]
    public void QuickDrawSettingsConfig_UsesThreeSecondAutoCloseDefault()
    {
        QuickDrawSettingsConfig settings = new();

        Assert.Equal(3, settings.AutoCloseTime);
    }

    [Fact]
    public void MainConfigModel_DeserializesQuickDrawAutoCloseTime()
    {
        const string json = """
                            {
                              "quick_draw_settings": {
                                "auto_close_time": 7
                              }
                            }
                            """;

        MainConfigModel? settings = JsonSerializer.Deserialize<MainConfigModel>(
            json,
            ConfigServiceBase.JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(7, settings.QuickDrawSettings.AutoCloseTime);
    }
}
