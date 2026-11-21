using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Draw;
using Xunit;

namespace SecRandom.Core.Tests;

/// <summary>
///     编排抽取是「抽取方式」而不是「抽取模式」：进入编排时先记住通道原来的重复策略，退出时还回去，
///     否则一次编排就会把用户的半重复档位静默压成不重复。
/// </summary>
public class DrawModeSelectionTests
{
    [Fact]
    public void EnteringOrchestrationRemembersTheRepeatPolicy()
    {
        var settings = new RollCallSettingsConfig { DrawMode = DrawMode.HalfRepeat, HalfRepeat = 5 };

        DrawModeSelection.SelectOrchestrated(settings);

        Assert.Equal(DrawMode.Orchestrated, settings.DrawMode);
        Assert.Equal(DrawMode.HalfRepeat, settings.SampledDrawMode);

        DrawModeSelection.LeaveOrchestrated(settings);

        Assert.Equal(DrawMode.HalfRepeat, settings.DrawMode);
        Assert.Equal(5, settings.HalfRepeat);
    }

    [Fact]
    public void ReSelectingOrchestrationKeepsTheFirstRecordedPolicy()
    {
        var settings = new LotterySettingsConfig { DrawMode = DrawMode.Repeat };

        DrawModeSelection.SelectOrchestrated(settings);
        DrawModeSelection.SelectOrchestrated(settings);

        Assert.Equal(DrawMode.Orchestrated, settings.DrawMode);
        Assert.Equal(DrawMode.Repeat, settings.SampledDrawMode);
    }

    [Fact]
    public void LeavingOrchestrationNeverRestoresOrchestration()
    {
        var settings = new QuickDrawSettingsConfig
        {
            DrawMode = DrawMode.Orchestrated,
            SampledDrawMode = DrawMode.Orchestrated
        };

        DrawModeSelection.LeaveOrchestrated(settings);

        Assert.Equal(DrawMode.NoRepeat, settings.DrawMode);
        Assert.Equal(DrawMode.Orchestrated, settings.SampledDrawMode);
    }

    [Fact]
    public void LeavingASampledChannelIsANoOp()
    {
        var settings = new RollCallSettingsConfig
        {
            DrawMode = DrawMode.NoRepeat,
            SampledDrawMode = DrawMode.HalfRepeat
        };

        DrawModeSelection.LeaveOrchestrated(settings);

        Assert.Equal(DrawMode.NoRepeat, settings.DrawMode);
        Assert.Equal(DrawMode.HalfRepeat, settings.SampledDrawMode);
    }

    [Fact]
    public void IsOrchestratedOnlyMatchesTheOrchestratedValue()
    {
        Assert.True(DrawModeSelection.IsOrchestrated(DrawMode.Orchestrated));
        Assert.False(DrawModeSelection.IsOrchestrated(DrawMode.Repeat));
        Assert.False(DrawModeSelection.IsOrchestrated(DrawMode.NoRepeat));
        Assert.False(DrawModeSelection.IsOrchestrated(DrawMode.HalfRepeat));
    }

    /// <summary>
    ///     「抽取模式」下拉框读的就是这个值：它必须永远是 0..2 的重复策略，否则三个选项的
    ///     下拉框会把越界索引压成 -1 再 TwoWay 写回配置，既清空下拉又丢掉编排状态。
    /// </summary>
    [Theory]
    [InlineData(DrawMode.Repeat, DrawMode.Repeat)]
    [InlineData(DrawMode.NoRepeat, DrawMode.NoRepeat)]
    [InlineData(DrawMode.HalfRepeat, DrawMode.HalfRepeat)]
    [InlineData(DrawMode.Orchestrated, DrawMode.Orchestrated)]
    public void ResolveRepeatPolicyIsAlwaysARepeatPolicy(DrawMode effectiveMode, DrawMode sampled)
    {
        var settings = new RollCallSettingsConfig { DrawMode = effectiveMode, SampledDrawMode = sampled };

        Assert.NotEqual(DrawMode.Orchestrated, DrawModeSelection.ResolveRepeatPolicy(settings));
    }

    [Theory]
    [InlineData(DrawMode.Repeat)]
    [InlineData(DrawMode.NoRepeat)]
    [InlineData(DrawMode.HalfRepeat)]
    public void SamplingChannelShowsItsEffectiveRepeatPolicy(DrawMode effectiveMode)
    {
        var settings = new QuickDrawSettingsConfig
        {
            DrawMode = effectiveMode,
            SampledDrawMode = DrawMode.HalfRepeat
        };

        Assert.Equal(effectiveMode, DrawModeSelection.ResolveRepeatPolicy(settings));
    }

    [Fact]
    public void OrchestratedChannelShowsTheRememberedRepeatPolicy()
    {
        var settings = new LotterySettingsConfig
        {
            DrawMode = DrawMode.Orchestrated,
            SampledDrawMode = DrawMode.HalfRepeat
        };

        Assert.Equal(DrawMode.HalfRepeat, DrawModeSelection.ResolveRepeatPolicy(settings));
    }

    [Fact]
    public void InvalidRememberedPolicyFallsBackToNoRepeat()
    {
        var settings = new LotterySettingsConfig
        {
            DrawMode = DrawMode.Orchestrated,
            SampledDrawMode = DrawMode.Orchestrated
        };

        Assert.Equal(DrawMode.NoRepeat, DrawModeSelection.ResolveRepeatPolicy(settings));
    }

    [Fact]
    public void ChoosingARepeatPolicyWhileSamplingChangesTheEffectiveModeToo()
    {
        var settings = new RollCallSettingsConfig { DrawMode = DrawMode.NoRepeat };

        DrawModeSelection.SetRepeatPolicy(settings, DrawMode.HalfRepeat);

        Assert.Equal(DrawMode.HalfRepeat, settings.DrawMode);
        Assert.Equal(DrawMode.HalfRepeat, settings.SampledDrawMode);
    }

    [Fact]
    public void ChoosingARepeatPolicyWhileOrchestratedOnlyRemembersIt()
    {
        var settings = new RollCallSettingsConfig
        {
            DrawMode = DrawMode.Orchestrated,
            SampledDrawMode = DrawMode.Repeat
        };

        DrawModeSelection.SetRepeatPolicy(settings, DrawMode.HalfRepeat);

        Assert.Equal(DrawMode.Orchestrated, settings.DrawMode);
        Assert.Equal(DrawMode.HalfRepeat, settings.SampledDrawMode);
        Assert.Equal(DrawMode.HalfRepeat, DrawModeSelection.ResolveRepeatPolicy(settings));
    }

    [Fact]
    public void SetRepeatPolicyRejectsOrchestration()
    {
        var settings = new RollCallSettingsConfig { DrawMode = DrawMode.NoRepeat };

        DrawModeSelection.SetRepeatPolicy(settings, DrawMode.Orchestrated);

        Assert.Equal(DrawMode.NoRepeat, settings.DrawMode);
        Assert.Equal(DrawMode.NoRepeat, settings.SampledDrawMode);
    }

    [Fact]
    public void SynchronizeFollowsTheEffectiveModeOnlyWhileSampling()
    {
        var sampling = new RollCallSettingsConfig
        {
            DrawMode = DrawMode.HalfRepeat,
            SampledDrawMode = DrawMode.Repeat
        };
        DrawModeSelection.SynchronizeRepeatPolicy(sampling);
        Assert.Equal(DrawMode.HalfRepeat, sampling.DrawMode);
        Assert.Equal(DrawMode.HalfRepeat, sampling.SampledDrawMode);

        var orchestrated = new RollCallSettingsConfig
        {
            DrawMode = DrawMode.Orchestrated,
            SampledDrawMode = DrawMode.Repeat
        };
        DrawModeSelection.SynchronizeRepeatPolicy(orchestrated);
        Assert.Equal(DrawMode.Orchestrated, orchestrated.DrawMode);
        Assert.Equal(DrawMode.Repeat, orchestrated.SampledDrawMode);
    }
}