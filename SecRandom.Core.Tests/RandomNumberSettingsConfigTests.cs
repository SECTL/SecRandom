using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Profiles;

namespace SecRandom.Core.Tests;

/// <summary>
///     The random-number page is the only place these defaults are edited, but imported settings can be
///     out of range. Normalize is the shared guard so the page and both draw entries agree on the bounds.
/// </summary>
public sealed class RandomNumberSettingsConfigTests
{
    [Fact]
    public void DefaultsAreOneToOneFiftyWithTemporaryOnlyReset()
    {
        var settings = new RandomNumberSettingsConfig();

        Assert.Equal(1, settings.RollCallRangeStart);
        Assert.Equal(50, settings.RollCallRangeEnd);
        Assert.Equal(1, settings.LotteryRangeStart);
        Assert.Equal(50, settings.LotteryRangeEnd);
        Assert.Equal(RandomNumberResetBehavior.TemporaryOnly, settings.ResetBehavior);
    }

    [Fact]
    public void NormalizeClampsNegativeStart()
    {
        var settings = new RandomNumberSettingsConfig
        {
            RollCallRangeStart = -5,
            RollCallRangeEnd = 50
        };

        Assert.True(settings.Normalize());
        Assert.Equal(0, settings.RollCallRangeStart);
        Assert.Equal(50, settings.RollCallRangeEnd);
    }

    [Fact]
    public void NormalizeCapsEndAtTheMaximumSpan()
    {
        var settings = new RandomNumberSettingsConfig
        {
            LotteryRangeStart = 10,
            LotteryRangeEnd = 500
        };

        Assert.True(settings.Normalize());

        var expectedEnd = 10 + RosterSelection.MaxNumberRangeSpan - 1;
        Assert.Equal(expectedEnd, settings.LotteryRangeEnd);
    }

    [Fact]
    public void NormalizeReturnsFalseWhenValuesAlreadyFit()
    {
        var settings = new RandomNumberSettingsConfig();

        Assert.False(settings.Normalize());
        Assert.Equal(1, settings.RollCallRangeStart);
        Assert.Equal(50, settings.LotteryRangeEnd);
    }
}