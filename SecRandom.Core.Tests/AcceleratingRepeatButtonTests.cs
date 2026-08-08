using SecRandom.Core.Controls;

namespace SecRandom.Core.Tests;

public sealed class AcceleratingRepeatButtonTests
{
    [Fact]
    public void HoldDurationAcceleratesRepeatCadence()
    {
        var initial = AcceleratingRepeatButton.CalculateInterval(TimeSpan.Zero);
        var afterOneSecond = AcceleratingRepeatButton.CalculateInterval(TimeSpan.FromSeconds(1));
        var afterThreeSeconds = AcceleratingRepeatButton.CalculateInterval(TimeSpan.FromSeconds(3));

        Assert.Equal(AcceleratingRepeatButton.InitialIntervalMilliseconds, initial);
        Assert.True(afterOneSecond < initial);
        Assert.True(afterThreeSeconds < afterOneSecond);
    }

    [Fact]
    public void RepeatCadenceHasALowerBound()
    {
        Assert.Equal(
            AcceleratingRepeatButton.MinimumIntervalMilliseconds,
            AcceleratingRepeatButton.CalculateInterval(TimeSpan.FromMinutes(1)));
    }
}
