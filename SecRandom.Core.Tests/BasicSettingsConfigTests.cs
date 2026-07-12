using System.Text.Json;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;

namespace SecRandom.Core.Tests;

public class BasicSettingsConfigTests
{
    [Fact]
    public void MainConfig_DefaultBasicWindowSettingsAreUsable()
    {
        MainConfigModel config = new();

        Assert.True(config.General.Basic.ShowStartupWindow);
        Assert.True(config.General.Basic.AutoSaveWindowSize);
        Assert.True(config.General.Basic.BackgroundResident);
        Assert.Equal(1200, config.General.Basic.MainWindowWidth);
        Assert.Equal(800, config.General.Basic.MainWindowHeight);
        Assert.False(config.General.Basic.MainWindowMaximized);
        Assert.Equal(TopmostMode.None, config.General.Basic.MainWindowTopmostMode);
    }

    [Fact]
    public void MainConfig_BasicWindowSettingsRoundTripThroughJson()
    {
        MainConfigModel config = new();
        config.General.Basic.MainWindowWidth = 1440;
        config.General.Basic.MainWindowHeight = 900;
        config.General.Basic.MainWindowMaximized = true;
        config.General.Basic.MainWindowTopmostMode = TopmostMode.Topmost;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(1440, restored.General.Basic.MainWindowWidth);
        Assert.Equal(900, restored.General.Basic.MainWindowHeight);
        Assert.True(restored.General.Basic.MainWindowMaximized);
        Assert.Equal(TopmostMode.Topmost, restored.General.Basic.MainWindowTopmostMode);
    }
}
