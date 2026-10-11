using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.Services.Mobile;
using SecRandom.ViewModels.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
///     控制端"远程抽取"页：选教室设备、读名单/奖池、下发抽取，页面本身只有布局，
///     状态与命令都在 <see cref="MobileRemoteDrawViewModel" /> 里（能脱离界面单测的部分都放那儿）。
/// </summary>
/// <remarks>
///     <para>
///         <b>这是手机版式的那个视图</b>：底部导航的第 5 档按 <see cref="MobilePageIds.RemoteDraw" /> 取它。
///         桌面与平板用桌面主页面版式的 <c>Views/MainPages/RemoteDrawPage</c>（左侧结果区 + 右侧控制区），
///         因为竖排表单在桌面尺寸上既浪费宽度也不像这个应用的其他主页。
///     </para>
///     <para>
///         <b>但 ViewModel 只有一份</b>（<see cref="MobileRemoteDrawViewModel" />）：协议、状态与命令两个
///         视图共用，UI 差异只是版式差异。谁为某个宿主复制一份 VM，协议一改就会有一边先烂掉。
///     </para>
/// </remarks>
[PageInfo(MobilePageIds.RemoteDraw, FluentIcons.SendFilled, location: PageLocation.Top)]
public sealed partial class MobileRemoteDrawPage : UserControl
{
    /// <summary>集控设置页的页面 ID（<c>[PageInfo]</c> 里注册的那个）。</summary>
    private const string ControlSettingsPageId = "settings.general.control";

    public MobileRemoteDrawPage(MobileRemoteDrawViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        ViewModel.ReenrollRequested += OnReenrollRequested;
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public MobileRemoteDrawViewModel ViewModel { get; }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 每次进页面都刷新一次：组与设备是"现在"的状态，用进页面时缓存的数据发命令只会撞上过期的节点。
        await ViewModel.InitializeAsync();
    }

    /// <summary>
    ///     「重新接入」：本页有接入卡片时（手机）就地落到卡片上，没有才导航去集控页；导航器也不可用时至少不崩。
    /// </summary>
    private void OnReenrollRequested(object? sender, EventArgs e)
    {
        // 手机上集控页根本不存在，导航过去是"点了没反应"——那种情况把焦点交给下面卡片的接入码输入框。
        if (ViewModel.ShowEnrollmentEntry)
        {
            this.FindControl<TextBox>("EnrollmentCodeBox")?.Focus();
            return;
        }

        // 只在这里按需取：构造函数必须保持"只有 ViewModel 一个参数"，两个视图共用一份 VM 的结构
        // 由 TabletControlHostTests 钉死，多一个参数就等于给这个结构开了个口子。
        if (IAppHost.TryGetService<IMobileSettingsNavigator>() is not { } navigator)
            return;

        _ = navigator.NavigateAsync(ControlSettingsPageId);
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        ViewModel.ReenrollRequested -= OnReenrollRequested;
        ViewModel.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
