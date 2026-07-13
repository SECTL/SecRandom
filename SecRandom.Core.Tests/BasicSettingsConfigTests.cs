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
        Assert.Equal(1000, config.General.Basic.SettingsWindowWidth);
        Assert.Equal(720, config.General.Basic.SettingsWindowHeight);
        Assert.False(config.General.Basic.SettingsWindowMaximized);
        Assert.Equal(TopmostMode.None, config.General.Basic.MainWindowTopmostMode);
    }

    [Fact]
    public void MainConfig_BasicWindowSettingsRoundTripThroughJson()
    {
        MainConfigModel config = new();
        config.General.Basic.MainWindowWidth = 1440;
        config.General.Basic.MainWindowHeight = 900;
        config.General.Basic.MainWindowMaximized = true;
        config.General.Basic.SettingsWindowWidth = 1280;
        config.General.Basic.SettingsWindowHeight = 840;
        config.General.Basic.SettingsWindowMaximized = true;
        config.General.Basic.MainWindowTopmostMode = TopmostMode.Topmost;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(1440, restored.General.Basic.MainWindowWidth);
        Assert.Equal(900, restored.General.Basic.MainWindowHeight);
        Assert.True(restored.General.Basic.MainWindowMaximized);
        Assert.Equal(1280, restored.General.Basic.SettingsWindowWidth);
        Assert.Equal(840, restored.General.Basic.SettingsWindowHeight);
        Assert.True(restored.General.Basic.SettingsWindowMaximized);
        Assert.Equal(TopmostMode.Topmost, restored.General.Basic.MainWindowTopmostMode);
    }

    [Fact]
    public void MainConfig_PreservesMaximizedStateWithSeparateNormalWindowSizes()
    {
        MainConfigModel config = new();
        config.General.Basic.MainWindowWidth = 1366;
        config.General.Basic.MainWindowHeight = 768;
        config.General.Basic.MainWindowMaximized = true;
        config.General.Basic.SettingsWindowWidth = 1024;
        config.General.Basic.SettingsWindowHeight = 700;
        config.General.Basic.SettingsWindowMaximized = true;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(1366, restored.General.Basic.MainWindowWidth);
        Assert.Equal(768, restored.General.Basic.MainWindowHeight);
        Assert.True(restored.General.Basic.MainWindowMaximized);
        Assert.Equal(1024, restored.General.Basic.SettingsWindowWidth);
        Assert.Equal(700, restored.General.Basic.SettingsWindowHeight);
        Assert.True(restored.General.Basic.SettingsWindowMaximized);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MainConfig_WindowSizeAcceptsLegacyValuesWithoutBreakingDeserialization(double width)
    {
        MainConfigModel config = new();
        config.General.Basic.MainWindowWidth = width;
        config.General.Basic.SettingsWindowHeight = width;

        string json = JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions);
        MainConfigModel? restored = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(width, restored.General.Basic.MainWindowWidth);
        Assert.Equal(width, restored.General.Basic.SettingsWindowHeight);
    }
}
