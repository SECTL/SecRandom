using System.Reflection;
using System.Text.RegularExpressions;
using SecRandom.Core.Attributes;
using SecRandom.Mobile;
using SecRandom.Services.ControlNode;
using SecRandom.ViewModels.Mobile;
using SecRandom.Views.Mobile;

namespace SecRandom.Core.Tests;

/// <summary>
///     平板同时是控制端与被控端时的宿主组合：角色矩阵、闸门选择、两个入口与注册差异。
/// </summary>
/// <remarks>
///     <para>
///         三种宿主只有两个输入（是否移动平台、平板是否用桌面主界面），因此这里先把
///         <see cref="AppHostShape" /> 的矩阵逐行钉死——它取代了原先散在 <c>BuildHost</c> 里的
///         <c>!isMobile</c> 判断，一旦有人把某个分支改回按平台判断，这些断言先红。
///     </para>
///     <para>
///         <b>为什么不真的跑一遍 <c>BuildHost</c> 再断言 DI</b>：<c>PagesRegistryService</c> 是**进程级静态**集合，
///         同一进程里建第二个 Host 会以"页面 id 已被占用"抛异常，而桌面/平板/手机三种形态必须各建一次；
///         再加上 <c>BuildHost</c> 会拉起插件管理器与整套 Avalonia 服务，单测里跑不动。
///         因此这里断言两件事：判定函数本身的**行为**（矩阵），以及 <c>BuildHost</c> **确实把它当作唯一判据**
///         （源码级接线断言：节点注册与闸门注册分别只出现在对应的那一个分支里）。
///     </para>
/// </remarks>
public sealed class TabletControlHostTests
{
    private const string AppSourcePath = @"SecRandom/App.axaml.cs";

    // ---------------------------------------------------------------- 宿主形态矩阵

    [Theory]
    //                   isMobile 桌面主界面 | 是移动 是平板 手机壳 跑节点 解锁闸门
    [InlineData(false, false, false, false, false, true, false)]   // 桌面
    [InlineData(true, true, true, true, false, true, false)]       // 平板
    [InlineData(true, false, true, false, true, false, true)]      // 手机
    public void 宿主形态_三种形态的角色矩阵(
        bool isMobile,
        bool usesDesktopMainView,
        bool expectedIsMobile,
        bool expectedDesktopMainView,
        bool expectedMobileShell,
        bool expectedRunsControlNode,
        bool expectedUnlockedGate)
    {
        var shape = AppHostShape.Resolve(isMobile, usesDesktopMainView);

        Assert.Equal(expectedIsMobile, shape.IsMobile);
        Assert.Equal(expectedDesktopMainView, shape.UsesDesktopMainView);
        Assert.Equal(expectedMobileShell, shape.UsesMobileShell);
        Assert.Equal(expectedRunsControlNode, shape.RunsControlNode);
        Assert.Equal(expectedUnlockedGate, shape.UsesUnlockedDrawGate);

        // 控制端入口：手机（底部）与平板（侧栏）都有，桌面没有。
        Assert.Equal(isMobile, shape.UsesRemoteDrawPage);
    }

    [Fact]
    public void 宿主形态_节点与解锁闸门互斥且恰好有一个()
    {
        foreach (var (isMobile, usesDesktopMainView) in new[] { (false, false), (true, true), (true, false) })
        {
            var shape = AppHostShape.Resolve(isMobile, usesDesktopMainView);

            // 两个标志同时为真＝既注册了真闸门又注册了解锁闸门；同时为假＝闸门根本没注册，
            // 而 IControlDrawGate 缺了会让整台 Host 起不来。两种都是致命错误，所以钉成"恰好一个"。
            Assert.True(
                shape.RunsControlNode ^ shape.UsesUnlockedDrawGate,
                $"isMobile={isMobile}, usesDesktopMainView={usesDesktopMainView} 时闸门选择不成立");
        }
    }

    [Fact]
    public void 宿主形态_平板用真闸门而手机用解锁闸门()
    {
        var tablet = AppHostShape.Resolve(isMobile: true, usesDesktopMainView: true);
        var phone = AppHostShape.Resolve(isMobile: true, usesDesktopMainView: false);
        var desktop = AppHostShape.Resolve(isMobile: false, usesDesktopMainView: false);

        // 硬约束：平板上有真的集控节点，因此必须用真闸门——用解锁闸门会让远程锁定失效、本地抽取被静默放行。
        Assert.False(tablet.UsesUnlockedDrawGate);
        Assert.True(tablet.RunsControlNode);

        // 反过来：手机没有节点，用真闸门等于凭空多出一个自己控制不了的锁。
        Assert.True(phone.UsesUnlockedDrawGate);
        Assert.False(phone.RunsControlNode);

        Assert.False(desktop.UsesUnlockedDrawGate);
        Assert.True(desktop.RunsControlNode);
    }

    [Fact]
    public void 宿主形态_非移动平台不会被误判成平板()
    {
        // 调用方可能从一个尚未初始化的平台根里读到 true；非移动平台上这个标志一律无效。
        var shape = AppHostShape.Resolve(isMobile: false, usesDesktopMainView: true);

        Assert.False(shape.UsesDesktopMainView);
        Assert.False(shape.UsesMobileShell);
        Assert.True(shape.RunsControlNode);
        Assert.False(shape.UsesUnlockedDrawGate);
        Assert.False(shape.UsesRemoteDrawPage);
    }

    // ---------------------------------------------------------------- 平板入口与手机共用同一份页面

    [Fact]
    public void 平板入口_远程抽取页带着主界面导航项所需的注册信息()
    {
        var attribute = typeof(MobileRemoteDrawPage).GetCustomAttribute<PageInfo>();

        // AddMainPage<T> 要求类型上带 [PageInfo]，否则注册时直接抛异常——平板侧栏入口依赖它。
        Assert.NotNull(attribute);
        Assert.Equal(MobilePageIds.RemoteDraw, attribute!.Id);
        Assert.False(attribute.IsSeparator);
    }

    [Fact]
    public void 平板入口_与手机入口是同一个页面同一个视图模型()
    {
        // 手机底部第 5 档与平板侧栏项都从 MobilePageIds.RemoteDraw 取页面；
        // 页面只有一个构造函数，参数就是那个共享的 ViewModel——为平板另写一套 UI/VM 时这里会先红。
        var constructors = typeof(MobileRemoteDrawPage).GetConstructors();
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(typeof(MobileRemoteDrawViewModel), parameter.ParameterType);
    }

    // ---------------------------------------------------------------- BuildHost 的接线（防止被误合并）

    [Fact]
    public void 接线_节点注册与闸门注册各自只有一个分支()
    {
        var source = ReadAppSource();

        var nodeBranch = SectionOf(source, "if (hostShape.RunsControlNode)", "else if (hostShape.UsesUnlockedDrawGate)");

        // 真有节点的宿主：真闸门 + 节点服务 + 集控设置页。
        Assert.Contains("services.AddSingleton<IControlDrawGate, ControlDrawGateService>();", nodeBranch, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IControlNodeStateStore, FileControlNodeStateStore>();", nodeBranch, StringComparison.Ordinal);
        Assert.Contains("services.AddSingleton<IControlCommandDispatcher, ControlCommandDispatcher>();", nodeBranch, StringComparison.Ordinal);
        Assert.Contains("services.AddHostedService<ControlNodeHostedService>();", nodeBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("UnlockedControlDrawGate", nodeBranch, StringComparison.Ordinal);

        var gateFallbackBranch = SectionOf(source, "else if (hostShape.UsesUnlockedDrawGate)", "services.AddSingleton<SecurityCredentialStore>();");

        // 没有节点的宿主：只有解锁闸门，一个节点服务都不许有。
        Assert.Contains("services.AddSingleton<IControlDrawGate, UnlockedControlDrawGate>();", gateFallbackBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlDrawGateService", gateFallbackBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlNodeHostedService", gateFallbackBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("ControlNodeClient", gateFallbackBranch, StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_平板注册侧栏入口而手机保留底部入口()
    {
        var source = ReadAppSource();

        // 判定必须来自宿主形态，而不是某个地方又写了一次 !isMobile。
        Assert.Contains("var hostShape = AppHostShape.Resolve(", source, StringComparison.Ordinal);
        Assert.Contains("var useMobileUI = hostShape.UsesMobileShell;", source, StringComparison.Ordinal);

        // 平板：远程抽取页注册成主界面侧栏的一项。
        Assert.Contains("services.AddMainPage<MobileRemoteDrawPage>(MobileResources.P_RemoteDraw);", source, StringComparison.Ordinal);

        // 手机：底部导航按 key 取同一个页面——这条注册不能被平板的入口取代。
        Assert.Contains(
            "services.AddKeyedTransient<UserControl, MobileRemoteDrawPage>(MobilePageIds.RemoteDraw);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_集控设置页跟着节点一起出现()
    {
        var source = ReadAppSource();

        // 两处且只有两处按"跑不跑节点"分支：节点服务一份、集控设置页一份。
        // 页面与它依赖的节点服务必须在同一个判据下出现——页面在、节点不在（或反过来）都是坏的组合。
        var guards = Regex.Matches(source, @"if \(hostShape\.RunsControlNode\)");
        Assert.Equal(2, guards.Count);

        var settingsGuardIndex = guards[1].Index;
        var settingsBranch = source[settingsGuardIndex..Math.Min(source.Length, settingsGuardIndex + 600)];

        Assert.Contains("services.AddSettingsPage<ControlSettingsPage>(", settingsBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("UnlockedControlDrawGate", settingsBranch, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 远程抽取的置前决策

    [Theory]
    //                  isDesktop 可见 最小化 | 还原 显示 激活
    // 桌面：与 RemoteDrawWindowPlan 完全一致（隐藏→显示、最小化→还原、已可见→只激活）。
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, true, true, true, false, true)]
    [InlineData(true, true, false, false, false, true)]
    // 平板（单窗口宿主）：没有窗口可碰，三件事一件都不做。
    [InlineData(false, false, false, false, false, false)]
    [InlineData(false, true, true, false, false, false)]
    public void 远程抽取置前_按宿主形态决定碰不碰窗口(
        bool isDesktop,
        bool isWindowVisible,
        bool isWindowMinimized,
        bool expectedRestore,
        bool expectedShow,
        bool expectedActivate)
    {
        var plan = RemoteDrawFocusPlan.Resolve(isDesktop, isWindowVisible, isWindowMinimized);

        Assert.Equal(isDesktop ? RemoteDrawSurface.DesktopWindow : RemoteDrawSurface.SingleViewHost, plan.Surface);
        Assert.Equal(expectedRestore, plan.Restore);
        Assert.Equal(expectedShow, plan.Show);
        Assert.Equal(expectedActivate, plan.Activate);
    }

    [Fact]
    public void 远程抽取置前_平板的窗口状态即使读到了也不使用()
    {
        // 单窗口宿主上窗口状态是没有意义的输入（读到的可能是一个残留的桌面窗口）；
        // 决策必须只看"是不是桌面"，否则平板上会出现"以为自己在操作窗口"的代码。
        var visible = RemoteDrawFocusPlan.Resolve(isDesktop: false, isWindowVisible: true, isWindowMinimized: true);
        var hidden = RemoteDrawFocusPlan.Resolve(isDesktop: false, isWindowVisible: false, isWindowMinimized: false);

        Assert.Equal(visible, hidden);
        Assert.Equal(RemoteDrawSurface.SingleViewHost, visible.Surface);
    }

    // ---------------------------------------------------------------- 工具

    /// <summary>取出 <paramref name="start" /> 之后、<paramref name="end" /> 之前的那一段源码。</summary>
    /// <remarks>
    ///     断言"某个注册只在某个分支里"只能按源码切片做：DI 容器要在真 Host 里才建得出来，
    ///     而静态的页面注册表不允许同一进程建第二个 Host。
    /// </remarks>
    private static string SectionOf(string source, string start, string end, bool includeStart = false)
    {
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"源码里找不到起点：{start}");

        var endIndex = source.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"源码里找不到终点：{end}");

        return includeStart
            ? source[startIndex..endIndex]
            : source[(startIndex + start.Length)..endIndex];
    }

    private static string ReadAppSource() =>
        File.ReadAllText(GetRepositoryPath(AppSourcePath));

    private static string GetRepositoryPath(string relativePath) => Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../..")),
        relativePath);
}
