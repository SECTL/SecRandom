using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Abstraction.Services.Presentation;

/// <summary>
///     动画样式选择里的一项：宿主内置的三种动画，或者插件贡献的一个动画。
/// </summary>
/// <param name="Id">
///     <see cref="DrawAnimationSelection.Host" /> 表示宿主内置动画；其他值是插件呈现器（
///     <see cref="IDrawResultPresenter.Id" />）的 id。
/// </param>
/// <param name="DisplayName">下拉框里显示的名字。</param>
/// <param name="BuiltInStyle">内置动画对应的枚举值；插件动画为 <c>null</c>。</param>
/// <param name="IsPlugin">是否是插件贡献的动画。</param>
public sealed record DrawAnimationOption(
    string Id,
    string DisplayName,
    DrawAnimationStyleMode? BuiltInStyle = null,
    bool IsPlugin = false);

/// <summary>
///     当前选择的内置动画哨兵值与相关约定。
/// </summary>
public static class DrawAnimationSelection
{
    /// <summary>选中宿主内置动画（具体哪一种由 <see cref="DrawAnimationStyleMode" /> 决定）。</summary>
    public const string Host = "host";
}

/// <summary>
///     插件贡献一个"抽签动画样式"：插件在 <c>PluginBase.Initialize</c> 里注册本接口，
///     宿主的动画样式下拉框就会多出一项；用户选中该项时，宿主只把结果呈现交给
///     id 等于 <see cref="Id" /> 的 <see cref="IDrawResultPresenter" />。
///     <para>
///         约定：<see cref="Id" /> 必须与插件呈现器的 <see cref="IDrawResultPresenter.Id" /> 完全一致，
///         否则选中后没有人接管，界面会退回宿主内置动画。
///     </para>
/// </summary>
public interface IDrawAnimationContribution
{
    /// <summary>项的唯一 id，必须等于对应呈现器的 id（大小写不敏感）。</summary>
    string Id { get; }

    /// <summary>下拉框里显示的名字（插件自己负责本地化）。</summary>
    string DisplayName { get; }

    /// <summary>排序权重，数值越大越靠前；宿主内置动画固定在插件动画之前。</summary>
    int Priority { get; }
}
