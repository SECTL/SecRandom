using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Icons;
using SecRandom.ViewModels.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
///     控制端"远程抽取"页：选教室设备、读名单/奖池、下发抽取，页面本身只有布局，
///     状态与命令都在 <see cref="MobileRemoteDrawViewModel" /> 里（能脱离界面单测的部分都放那儿）。
/// </summary>
/// <remarks>
///     <para>
///         <b>同一份页面对应两个入口</b>：手机底部导航的第 5 档按 <see cref="MobilePageIds.RemoteDraw" />
///         取这个页面；平板用桌面主界面（<c>MainView</c>），没有底部导航栏，于是把同一个键注册成**主界面侧栏的一项**
///         （<c>AddMainPage</c>，因此需要下面这个 <see cref="PageInfo" />）。
///         两个入口共用同一个页面类型与同一个 ViewModel——为平板另写一套 UI 或 VM，
///         等于让协议一改就要改两处。
///     </para>
/// </remarks>
[PageInfo(MobilePageIds.RemoteDraw, FluentIcons.SendFilled, location: PageLocation.Top)]
public sealed partial class MobileRemoteDrawPage : UserControl
{
    public MobileRemoteDrawPage(MobileRemoteDrawViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = this;
        InitializeComponent();
        Loaded += OnLoaded;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public MobileRemoteDrawViewModel ViewModel { get; }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // 每次进页面都刷新一次：组与设备是"现在"的状态，用进页面时缓存的数据发命令只会撞上过期的节点。
        await ViewModel.InitializeAsync();
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) =>
        ViewModel.Dispose();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
