using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.Presentation;

namespace SecRandom.Core.Tests;

/// <summary>
///     插件被卸载/禁用后，它贡献的"动画样式"选项就没了，配置里存的那个 id 也随之变成死值：
///     清理逻辑必须把它退回宿主内置动画（用户的抽取照旧有演出），已有的选择不能被误伤。
/// </summary>
public class AnimationSelectionFallbackTests
{
    private const string PluginAnimationId = "plugin.test.recruit.presenter";

    [Fact]
    public void MissingContribution_ResetsEveryDrawSectionToHost()
    {
        var config = new MainConfigModel();
        config.DefaultDrawSettings.PluginAnimationId = PluginAnimationId;
        config.RollCallSettings.PluginAnimationId = PluginAnimationId;
        config.QuickDrawSettings.PluginAnimationId = PluginAnimationId;
        config.LotterySettings.PluginAnimationId = PluginAnimationId;

        var reset = AnimationSelectionFallback.Apply(config, []);

        Assert.Equal(4, reset);
        Assert.Equal(DrawAnimationSelection.Host, config.DefaultDrawSettings.PluginAnimationId);
        Assert.Equal(DrawAnimationSelection.Host, config.RollCallSettings.PluginAnimationId);
        Assert.Equal(DrawAnimationSelection.Host, config.QuickDrawSettings.PluginAnimationId);
        Assert.Equal(DrawAnimationSelection.Host, config.LotterySettings.PluginAnimationId);
    }

    [Fact]
    public void PresentContribution_KeepsSelectionAndBuiltInStyle()
    {
        var config = new MainConfigModel();
        config.DefaultDrawSettings.PluginAnimationId = PluginAnimationId;
        // 插件 id 大小写不敏感：宿主按 IgnoreCase 比对，清理也必须一样。
        config.RollCallSettings.PluginAnimationId = PluginAnimationId.ToUpperInvariant();
        config.RollCallSettings.AnimationStyle = DrawAnimationStyleMode.HorizontalShake;

        var reset = AnimationSelectionFallback.Apply(config, [new FakeContribution(PluginAnimationId)]);

        Assert.Equal(0, reset);
        Assert.Equal(PluginAnimationId, config.DefaultDrawSettings.PluginAnimationId);
        Assert.Equal(PluginAnimationId.ToUpperInvariant(), config.RollCallSettings.PluginAnimationId);
        Assert.Equal(DrawAnimationStyleMode.HorizontalShake, config.RollCallSettings.AnimationStyle);
    }

    [Fact]
    public void BuiltInSelectionAndBlankValue_AreLeftAlone()
    {
        var config = new MainConfigModel();
        config.RollCallSettings.PluginAnimationId = DrawAnimationSelection.Host;
        // 空值来自手改过的旧配置，宿主会当内置动画用，清理不必插手。
        config.QuickDrawSettings.PluginAnimationId = string.Empty;

        var reset = AnimationSelectionFallback.Apply(config, []);

        Assert.Equal(0, reset);
        Assert.Equal(DrawAnimationSelection.Host, config.RollCallSettings.PluginAnimationId);
        Assert.Equal(string.Empty, config.QuickDrawSettings.PluginAnimationId);
    }

    [Fact]
    public void NullContributions_TreatEveryPluginSelectionAsMissing()
    {
        var config = new MainConfigModel();
        config.LotterySettings.PluginAnimationId = PluginAnimationId;

        Assert.Equal(1, AnimationSelectionFallback.Apply(config, null));
        Assert.Equal(DrawAnimationSelection.Host, config.LotterySettings.PluginAnimationId);

        // 没有配置（例如移动端首次启动）时不能炸。
        Assert.Equal(0, AnimationSelectionFallback.Apply(null, null));
    }

    private sealed record FakeContribution(string Id, string DisplayName = "测试动画", int Priority = 100)
        : IDrawAnimationContribution;
}
