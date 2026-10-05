using SecRandom.Core.Models;
using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Tests;

/// <summary>
///     计时器自动缩小：在页面上**一段时间没有任何操作**之后缩成小窗，
///     三种模式各有开关（默认只开倒计时），且同一段无操作只缩一次。
/// </summary>
public sealed class TimerSettingsConfigTests
{
    [Fact]
    public void 默认只开倒计时且阈值为十秒()
    {
        var settings = new MainConfigModel().TimerSettings;

        Assert.True(settings.AutoMiniWindowCountdownEnabled);
        Assert.False(settings.AutoMiniWindowStopwatchEnabled);
        Assert.False(settings.AutoMiniWindowClockEnabled);
        Assert.Equal(10, settings.AutoMiniWindowAfterSeconds);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(9.9, false)]
    [InlineData(10, true)]
    [InlineData(600, true)]
    public void 无操作走过阈值才缩(double idleSeconds, bool expected)
    {
        var settings = new TimerSettingsConfig();

        Assert.Equal(expected, settings.ShouldShrinkToMiniWindow(true, idleSeconds, alreadyTriggered: false));
    }

    /// <summary>
    ///     开关是**按模式**传进来的：没开的模式（默认的秒表与时钟）无论停多久都不缩。
    /// </summary>
    [Fact]
    public void 没开的模式不缩()
    {
        var settings = new TimerSettingsConfig();

        Assert.False(settings.ShouldShrinkToMiniWindow(false, 600, alreadyTriggered: false));
        Assert.False(settings.ShouldShrinkToMiniWindow(
            settings.AutoMiniWindowStopwatchEnabled, 600, alreadyTriggered: false));
        Assert.False(settings.ShouldShrinkToMiniWindow(
            settings.AutoMiniWindowClockEnabled, 600, alreadyTriggered: false));
        Assert.True(settings.ShouldShrinkToMiniWindow(
            settings.AutoMiniWindowCountdownEnabled, 600, alreadyTriggered: false));
    }

    /// <summary>
    ///     同一次无操作只缩一次：用户关掉小窗会把大窗还原回来，这里要是还缩，
    ///     每 33ms 的刷新都会再缩一次，大窗就再也留不住了（下一次操作才会重新开始算）。
    /// </summary>
    [Fact]
    public void 同一段无操作只缩一次()
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

    /// <summary>手改配置文件写出 0 秒时，不应该变成"页面一打开就缩"。</summary>
    [Fact]
    public void 越界阈值不会让页面一打开就缩()
    {
        var settings = new TimerSettingsConfig { AutoMiniWindowAfterSeconds = 0 };

        Assert.False(settings.ShouldShrinkToMiniWindow(true, 0, alreadyTriggered: false));
        Assert.True(settings.ShouldShrinkToMiniWindow(true, 1, alreadyTriggered: false));
    }
}
