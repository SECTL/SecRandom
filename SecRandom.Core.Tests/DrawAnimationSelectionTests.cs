using SecRandom.Core.Abstraction.Services.Presentation;
using SecRandom.Core.Services.Presentation;

namespace SecRandom.Core.Tests;

/// <summary>
///     "动画样式"下拉框决定谁接管揭晓：
///     选内置动画时插件完全不介入，选某个插件动画时只有那个插件可以接管，
///     请求里没带选择（旧配置 / 其它调用方）时保持插件自动接管的旧行为。
/// </summary>
public class DrawAnimationSelectionTests
{
    private const string PluginAnimationId = "plugin.test.recruit.presenter";

    [Fact]
    public void RequestWithoutSelection_LetsPluginTakeOver()
    {
        var presenter = new RecordingPresenter(PluginAnimationId);
        var service = new ResultPresentationService([presenter]);

        var request = NewRequest();

        Assert.Same(presenter, service.Resolve(request));
        Assert.True(service.PresentAsync(request).GetAwaiter().GetResult().Handled);
        Assert.Equal(1, presenter.PresentCount);
    }

    [Fact]
    public void HostAnimationSelection_BlocksPluginPresenter()
    {
        var presenter = new RecordingPresenter(PluginAnimationId);
        var service = new ResultPresentationService([presenter]);

        var request = NewRequest(DrawAnimationSelection.Host);

        Assert.Null(service.Resolve(request));
        Assert.False(service.PresentAsync(request).GetAwaiter().GetResult().Handled);
        Assert.Equal(0, presenter.PresentCount);
    }

    [Fact]
    public void MatchingPluginAnimationId_LetsThatPresenterTakeOver()
    {
        var presenter = new RecordingPresenter(PluginAnimationId);
        var service = new ResultPresentationService([presenter]);

        var request = NewRequest(PluginAnimationId);

        Assert.Same(presenter, service.Resolve(request));
        Assert.True(service.PresentAsync(request).GetAwaiter().GetResult().Handled);
        Assert.Equal(1, presenter.PresentCount);
    }

    [Fact]
    public void PluginAnimationId_IgnoresCase()
    {
        var presenter = new RecordingPresenter(PluginAnimationId);
        var service = new ResultPresentationService([presenter]);

        Assert.Same(presenter, service.Resolve(NewRequest(PluginAnimationId.ToUpperInvariant())));
    }

    [Fact]
    public void AnotherPluginsAnimationId_DoesNotLetPresenterTakeOver()
    {
        var presenter = new RecordingPresenter(PluginAnimationId);
        var service = new ResultPresentationService([presenter]);

        var request = NewRequest("plugin.other.animation");

        Assert.Null(service.Resolve(request));
        Assert.False(service.PresentAsync(request).GetAwaiter().GetResult().Handled);
        Assert.Equal(0, presenter.PresentCount);
    }

    [Fact]
    public void SelectedPresenterThatDeclines_DoesNotBlockOtherPresenters()
    {
        var selected = new RecordingPresenter("plugin.a", wants: false);
        var other = new RecordingPresenter("plugin.b");
        var service = new ResultPresentationService([selected, other]);

        // 选中 a，a 自己不要这一轮 → b 仍然有机会；但 b 不是被选中的那个，所以也不接管。
        var selectedRequest = NewRequest("plugin.a");
        Assert.Null(service.Resolve(selectedRequest));
        Assert.False(service.PresentAsync(selectedRequest).GetAwaiter().GetResult().Handled);
        Assert.Equal(0, other.PresentCount);
    }

    [Fact]
    public void HostSelectionConstant_IsStable()
    {
        // 这个字面量会被写进用户配置，改动等于让老配置失效。
        Assert.Equal("host", DrawAnimationSelection.Host);
    }

    [Fact]
    public void BuiltInOption_IsNotMarkedAsPlugin()
    {
        var option = new DrawAnimationOption(DrawAnimationSelection.Host, "内置",
            SecRandom.Core.Enums.Configs.DrawAnimationStyleMode.DirectRotate);

        Assert.False(option.IsPlugin);
        Assert.NotNull(option.BuiltInStyle);

        // 插件项的写法：没有内置枚举值，标记成插件。
        var plugin = new DrawAnimationOption("plugin.test.animation", "插件动画", null, true);
        Assert.True(plugin.IsPlugin);
        Assert.Null(plugin.BuiltInStyle);
    }

    private static DrawPresentationRequest NewRequest(string pluginAnimationId = "")
    {
        return new DrawPresentationRequest
        {
            Channel = DrawPresentationChannel.RollCall,
            Phase = DrawPresentationPhase.Reveal,
            ListName = "点名名单",
            PluginAnimationId = pluginAnimationId
        };
    }

    private sealed class RecordingPresenter(string id, bool wants = true) : IDrawResultPresenter
    {
        public string Id { get; } = id;

        public string DisplayName => Id;

        public int PresentCount { get; private set; }

        public bool CanPresent(DrawPresentationRequest request)
        {
            return wants;
        }

        public Task<DrawPresentationDecision> PresentAsync(DrawPresentationRequest request,
            CancellationToken cancellationToken = default)
        {
            PresentCount++;
            return Task.FromResult(DrawPresentationDecision.Taken());
        }
    }
}
