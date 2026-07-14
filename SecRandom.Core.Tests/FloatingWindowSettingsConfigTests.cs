using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Models;

namespace SecRandom.Core.Tests;

public class FloatingWindowSettingsConfigTests
{
    [Fact]
    public void MainConfig_FloatingWindowButtonSelectionsRoundTripThroughJson()
    {
        MainConfigModel config = new();
        config.FloatingWindowSettings.ShowRollCallButton = false;
        config.FloatingWindowSettings.ShowQuickDrawButton = false;
        config.FloatingWindowSettings.ShowLotteryButton = true;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.False(restored.FloatingWindowSettings.ShowRollCallButton);
        Assert.False(restored.FloatingWindowSettings.ShowQuickDrawButton);
        Assert.True(restored.FloatingWindowSettings.ShowLotteryButton);
    }
}
