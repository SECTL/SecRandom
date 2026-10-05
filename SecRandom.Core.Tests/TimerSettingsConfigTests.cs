using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Tests;

/// <summary>
///     计时器自动缩小：阈值按"计时开始之后走过了多久"算（不是剩余多少），且一次计时只缩一次。
/// </summary>
public sealed class TimerSettingsConfigTests
{
    [Fact]
    public void 默认开启且阈值为十秒()
    {
        var settings = new MainConfigModel().TimerSettings;

        Assert.True(settings.AutoMiniWindowEnabled);
        Assert.Equal(10, settings.AutoMiniWindowAfterSeconds);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9.9, false)]
    [InlineData(10, true)]
    [InlineData(600, true)]
    public void 计时走过阈值才缩(double runningSeconds, bool expected)
    {
        var settings = new TimerSettingsConfig();

        Assert.Equal(expected, settings.ShouldShrinkToMiniWindow(true, runningSeconds, alreadyTriggered: false));
    }

    [Fact]
    public void 关掉开关或没在计时都不缩()
    {
        Assert.False(new TimerSettingsConfig { AutoMiniWindowEnabled = false }
            .ShouldShrinkToMiniWindow(true, 600, alreadyTriggered: false));
        Assert.False(new TimerSettingsConfig()
            .ShouldShrinkToMiniWindow(false, 600, alreadyTriggered: false));
    }

    /// <summary>
    ///     同一次计时只缩一次：用户关掉小窗会把大窗还原回来，这里要是还缩，
    ///     每 33ms 的刷新都会再缩一次，大窗就再也留不住了。
    /// </summary>
    [Fact]
    public void 同一次计时只缩一次()
    {
        var settings = new TimerSettingsConfig();

        Assert.False(settings.ShouldShrinkToMiniWindow(true, 600, alreadyTriggered: true));
    }

    [Theory]
    [InlineData(0, TimerSettingsConfig.MinAutoMiniWindowSeconds)]
    [InlineData(-5, TimerSettingsConfig.MinAutoMiniWindowSeconds)]
    [InlineData(1, 1)]
    [InlineData(45, 45)]
    [InlineData(3600, TimerSettingsConfig.MaxAutoMiniWindowSeconds)]
    [InlineData(99999, TimerSettingsConfig.MaxAutoMiniWindowSeconds)]
    public void 越界阈值被夹回范围(int stored, int expected)
    {
        var settings = new TimerSettingsConfig { AutoMiniWindowAfterSeconds = stored };

        Assert.Equal(expected, settings.EffectiveAutoMiniWindowSeconds);
    }

    /// <summary>手改配置文件写出 0 秒时，不应该变成"一开始就缩"。</summary>
    [Fact]
    public void 越界阈值不会让计时一开始就缩()
    {
        var settings = new TimerSettingsConfig { AutoMiniWindowAfterSeconds = 0 };

        Assert.False(settings.ShouldShrinkToMiniWindow(true, 0, alreadyTriggered: false));
        Assert.True(settings.ShouldShrinkToMiniWindow(true, 1, alreadyTriggered: false));
    }
}
