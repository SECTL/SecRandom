using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Attributes;
using SecRandom.Core.Extensions.Registry;
using SecRandom.Core.Services;
using SecRandom.Core.Services.ControlNode;
using SecRandom.Mobile;
using SecRandom.Services.ControlNode;
using SecRandom.ViewModels.Mobile;
using SecRandom.Views.MainPages;
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
    //                   isMobile 桌面主界面 | 是移动 是平板 手机壳 跑节点 解锁闸门 侧栏页 底部档 默认隐藏
    [InlineData(false, false, false, false, false, true, false, true, false, true)]   // 桌面
    [InlineData(true, true, true, true, false, true, false, true, false, false)]      // 平板
    [InlineData(true, false, true, false, true, false, true, false, true, false)]     // 手机
    public void 宿主形态_三种形态的角色矩阵(
        bool isMobile,
        bool usesDesktopMainView,
        bool expectedIsMobile,
        bool expectedDesktopMainView,
        bool expectedMobileShell,
        bool expectedRunsControlNode,
        bool expectedUnlockedGate,
        bool expectedRemoteDrawSidebar,
        bool expectedRemoteDrawBottom,
        bool expectedRemoteDrawHiddenByDefault)
    {
        var shape = AppHostShape.Resolve(isMobile, usesDesktopMainView);

        Assert.Equal(expectedIsMobile, shape.IsMobile);
        Assert.Equal(expectedDesktopMainView, shape.UsesDesktopMainView);
        Assert.Equal(expectedMobileShell, shape.UsesMobileShell);
        Assert.Equal(expectedRunsControlNode, shape.RunsControlNode);
        Assert.Equal(expectedUnlockedGate, shape.UsesUnlockedDrawGate);

        // 远程抽取页：**三个宿主都有**，只是外壳不同——桌面/平板是主界面侧栏主页面，手机是底部第 5 档。
        Assert.Equal(expectedRemoteDrawSidebar, shape.UsesRemoteDrawSidebarPage);
        Assert.Equal(expectedRemoteDrawBottom, shape.UsesRemoteDrawBottomTab);
        Assert.True(shape.UsesRemoteDrawSidebarPage ^ shape.UsesRemoteDrawBottomTab);

        // 默认隐藏只针对桌面：平板/手机维持升级前就有的样子（教室里平板就是控制台，不能突然少一页）。
        Assert.Equal(expectedRemoteDrawHiddenByDefault, shape.RemoteDrawEntryHiddenByDefault);
    }

    [Theory]
    //                  桌面 开关 | 可见
    [InlineData(true, false, false)]   // 桌面默认关 → 看不见
    [InlineData(true, true, true)]     // 桌面用户打开 → 看得见
    [InlineData(false, false, true)]   // 平板/手机不受这个开关影响：没有开关也必须看得见
    [InlineData(false, true, true)]
    public void 可见性_只有桌面受开关约束(bool isDesktop, bool enabledByUser, bool expectedVisible)
    {
        Assert.Equal(expectedVisible, AppHostShape.IsRemoteDrawEntryVisible(isDesktop, enabledByUser));
    }

    [Fact]
    public void 可见性_默认关闭且状态文件缺省即关闭()
    {
        // 模型默认值就是关：新装/升级后不该凭空多出一个会把本机令牌发往控制面的入口。
        Assert.False(new ControlNodeState().RemoteDrawPageEnabled);

        // 持久化层缺省同样是关（旧 node-state.json 里没有这个字段）。
        var storeSource = File.ReadAllText(GetRepositoryPath(@"SecRandom.Core/Services/ControlNode/FileControlNodeStateStore.cs"));
        Assert.Contains("RemoteDrawPageEnabled = stored?.RemoteDrawPageEnabled ?? false", storeSource, StringComparison.Ordinal);
        Assert.Contains("RemoteDrawPageEnabled = state.RemoteDrawPageEnabled", storeSource, StringComparison.Ordinal);
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
        // 桌面照样有远程抽取页，只是默认藏着（见 可见性_ 那组用例）。
        Assert.True(shape.UsesRemoteDrawSidebarPage);
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
    public void 接线_桌面与平板注册侧栏入口而手机保留底部入口()
    {
        var source = ReadAppSource();

        // 判定必须来自宿主形态，而不是某个地方又写了一次 !isMobile。
        Assert.Contains("var hostShape = AppHostShape.Resolve(", source, StringComparison.Ordinal);
        Assert.Contains("var useMobileUI = hostShape.UsesMobileShell;", source, StringComparison.Ordinal);

        // 桌面 + 平板：远程抽取页注册成主界面侧栏的一项（桌面只是默认隐藏它，注册永远都在）。
        Assert.Contains("if (hostShape.UsesRemoteDrawSidebarPage)", source, StringComparison.Ordinal);
        Assert.Contains("services.AddMainPage<RemoteDrawPage>(MobileResources.P_RemoteDraw);", source, StringComparison.Ordinal);

        // 手机：底部导航按 key 取**手机视图**——两个视图各注册在自己的宿主上，不存在第三份。
        Assert.Contains(
            "services.AddKeyedTransient<UserControl, MobileRemoteDrawPage>(MobilePageIds.RemoteDraw);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "services.AddKeyedTransient<UserControl, RemoteDrawPage>",
            source,
            StringComparison.Ordinal);

        // ViewModel 必须注册在**共享**分支，并且拿得到本机节点身份（手机没有节点 → GetService 返回 null，
        // 设备列表里一台都不标"本机"）。它原来只写在 `if (isMobile)` 里，桌面因此解析不到。
        Assert.Contains("new MobileRemoteDrawViewModel(", source, StringComparison.Ordinal);
        Assert.Contains("provider.GetService<IControlNodeStateStore>()", source, StringComparison.Ordinal);
        var mobileOnlyViews = SectionOf(source, "// 杂项 Views", "// 界面 Views");
        Assert.DoesNotContain("MobileRemoteDrawViewModel", mobileOnlyViews, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- 一份 VM + 两个视图

    [Fact]
    public void 两个视图_构造函数指向同一个ViewModel()
    {
        // 这是"一份 VM 两个视图"的结构性钉子：两个页面的构造函数参数都是同一个 VM 类型。
        // 谁哪天为桌面复制一份 VM，这里立刻红——协议逻辑分叉正是这个项目吃过两次亏的地方。
        var phone = Assert.Single(typeof(MobileRemoteDrawPage).GetConstructors()).GetParameters();
        var desktop = Assert.Single(typeof(RemoteDrawPage).GetConstructors()).GetParameters();

        Assert.Equal(typeof(MobileRemoteDrawViewModel), Assert.Single(phone).ParameterType);
        Assert.Equal(typeof(MobileRemoteDrawViewModel), Assert.Single(desktop).ParameterType);

        // 页面类型本身必须是两个，且分居桌面主页面目录与手机视图目录：一眼能看出没有第三份。
        Assert.NotEqual(typeof(MobileRemoteDrawPage), typeof(RemoteDrawPage));
        Assert.Equal("SecRandom.Views.Mobile", typeof(MobileRemoteDrawPage).Namespace);
        Assert.Equal("SecRandom.Views.MainPages", typeof(RemoteDrawPage).Namespace);

        // 两个视图共用同一个页面 id（手机底部档 / 桌面侧栏项），语义不变。
        Assert.Equal(MobilePageIds.RemoteDraw, typeof(RemoteDrawPage).GetCustomAttribute<PageInfo>()!.Id);
        Assert.Equal(MobilePageIds.RemoteDraw, typeof(MobileRemoteDrawPage).GetCustomAttribute<PageInfo>()!.Id);
    }

    [Fact]
    public void 侧栏顺序_远程抽取紧跟在抽奖之后()
    {
        var source = ReadAppSource();

        // 顺序由"注册顺序 + PageLocation"决定：远程抽取必须紧跟在抽奖注册之后，且与抽奖同一个位置分组
        // （这个应用里桌面主页都在 Bottom 组，Top 组是空的——把它注册到 Top 会跑到侧栏最上面去）。
        var lotteryIndex = source.IndexOf("services.AddMainPage<LotteryPage>(", StringComparison.Ordinal);
        var remoteIndex = source.IndexOf("services.AddMainPage<RemoteDrawPage>(", StringComparison.Ordinal);
        var historyIndex = source.IndexOf("services.AddMainPage<HistoryPage>(", StringComparison.Ordinal);

        Assert.True(lotteryIndex >= 0 && remoteIndex >= 0 && historyIndex >= 0, "找不到三条主页面注册");
        Assert.True(
            lotteryIndex < remoteIndex && remoteIndex < historyIndex,
            "注册顺序必须是 抽奖 → 远程抽取 → 历史（实际："
            + $"lottery={lotteryIndex}, remoteDraw={remoteIndex}, history={historyIndex}）");

        // 位置分组与抽奖一致（Bottom），否则侧栏里不会挨在一起。
        Assert.Equal(
            typeof(LotteryPage).GetCustomAttribute<PageInfo>()!.Location,
            typeof(RemoteDrawPage).GetCustomAttribute<PageInfo>()!.Location);

        // 注册顺序确实就是侧栏顺序：注册表按调用顺序追加，因此上一条"紧随其后"＝侧栏里紧随其后。
        PagesRegistryService.MainItems.Clear();
        try
        {
            var services = new ServiceCollection();
            services.AddMainPage<RollCallPage>("点名");
            services.AddMainPage<LotteryPage>("抽奖");
            services.AddMainPage<RemoteDrawPage>("远程抽取");
            services.AddMainPage<HistoryPage>("历史");

            var ids = PagesRegistryService.MainItems.Select(info => info.Id).ToList();
            var lotteryAt = ids.IndexOf("main.lottery");
            var remoteAt = ids.IndexOf(MobilePageIds.RemoteDraw);

            Assert.True(lotteryAt >= 0 && remoteAt >= 0, $"注册表里缺少页面：{string.Join(" → ", ids)}");
            Assert.True(
                remoteAt == lotteryAt + 1,
                $"期望「远程抽取」紧跟在「抽奖」之后，实际顺序：{string.Join(" → ", ids)}");
        }
        finally
        {
            PagesRegistryService.MainItems.Clear();
        }
    }

    [Fact]
    public void 桌面版式_页面不重复报名字而导航标签保留()
    {
        var page = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainPages/RemoteDrawPage.axaml"));

        // 页内不再有"远程抽取"标题（连资源键都不引用）：标题由 MainView 的 TitleContainer 承担，
        // 与点名/抽奖页的 title-hidden 一致。以后谁"顺手加回来"这里会红。
        Assert.DoesNotContain("P_RemoteDraw", page, StringComparison.Ordinal);
        Assert.DoesNotContain("N_RemoteDraw", page, StringComparison.Ordinal);

        // 让外壳不画标题行的开关就是 PageInfo.HidePageTitle（MainView.axaml 绑 SelectedPageInfo.HidePageTitle）。
        Assert.True(typeof(RemoteDrawPage).GetCustomAttribute<PageInfo>()!.HidePageTitle);

        // 侧栏那一条是导航标签，必须继续用同一个资源键——删的是页内标题，不是导航名。
        var source = ReadAppSource();
        Assert.Contains(
            "services.AddMainPage<RemoteDrawPage>(MobileResources.P_RemoteDraw);",
            source,
            StringComparison.Ordinal);

        // 标题行消失后内容会顶到内容区上沿：两栏本身留了外边距，首行是结果计数/空态，不会贴边、也不会空一条。
        Assert.Contains("Margin=\"16\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.DrawnCountText", page, StringComparison.Ordinal);
    }

    [Fact]
    public void 桌面版式_结果区不重复设备信息与刷新入口()
    {
        var xaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainPages/RemoteDrawPage.axaml"));

        // 按"右：控制区"注释切两半：左侧结果区 / 右侧控制区。
        var split = xaml.IndexOf("============ 右：控制区", StringComparison.Ordinal);
        Assert.True(split > 0, "找不到右侧控制区的分界注释");
        var resultArea = xaml[..split];
        var controlArea = xaml[split..];

        // 结果区不再有"抽到了"标题、也不再有当前设备摘要：当前设备在右侧控制区已经有一份。
        Assert.DoesNotContain("RD_Drawn", resultArea, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.DeviceSummary", resultArea, StringComparison.Ordinal);

        // 结果计数是**真实回执数据**，必须留着（防止"删多了"）。
        Assert.Contains("ViewModel.DrawnCountText", resultArea, StringComparison.Ordinal);

        // 同一动作只在一个地方出现：刷新只在控制区，结果区不再绑刷新命令。
        Assert.DoesNotContain("RefreshCommand", resultArea, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", controlArea, StringComparison.Ordinal);

        // 控制区顶部那行引导语也去掉了：第一项直接是「抽取类型」（手机页仍保留它，那边只有这一句引导）。
        Assert.DoesNotContain("Resources.RD_Hint", xaml, StringComparison.Ordinal);
        var phoneXaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));
        Assert.Contains("Resources.RD_Hint", phoneXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void 手机版式_结果区在控制项之前且不重复底部文案()
    {
        var xaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));

        // 结构断言：结果区（DrawnMembers 所在的那个 Border）必须出现在第一个控制项之前。
        var resultIndex = xaml.IndexOf("ViewModel.DrawnMembers", StringComparison.Ordinal);
        var firstControlIndex = xaml.IndexOf("Resources.RD_Kind", StringComparison.Ordinal);
        Assert.True(resultIndex > 0 && firstControlIndex > 0, "找不到结果区或第一个控制项");
        Assert.True(
            resultIndex < firstControlIndex,
            $"手机页结果区必须排在控制项之前（result={resultIndex}, firstControl={firstControlIndex}）");

        // 结果区封顶且可滚动：结果多时不许把下面的控制项顶出屏幕。
        Assert.Contains("MaxHeight=\"200\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);

        // 抽取按钮下方不再重复结果/失败文案（只留进行中状态），也照样不放刷新入口。
        var drawButtonIndex = xaml.IndexOf("ViewModel.DrawCommand", StringComparison.Ordinal);
        Assert.True(drawButtonIndex > 0, "找不到抽取按钮");
        var belowDrawButton = xaml[drawButtonIndex..];
        Assert.DoesNotContain("ViewModel.StatusText", belowDrawButton, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.DrawnMembers", belowDrawButton, StringComparison.Ordinal);
        Assert.DoesNotContain("ResultPlaceholderTitle", belowDrawButton, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsBusy", belowDrawButton, StringComparison.Ordinal);

        // 顶部结果区不放刷新（刷新在控制区）。
        var resultArea = xaml[..firstControlIndex];
        Assert.DoesNotContain("RefreshCommand", resultArea, StringComparison.Ordinal);
        Assert.Contains("RefreshCommand", xaml[firstControlIndex..], StringComparison.Ordinal);
    }

    [Fact]
    public void 空态与提示文案_两个视图共用同一份且三语齐全()
    {
        // 文案来源在 VM 里（两个视图都绑它），视图不再各自引资源键——否则改一次要改两处。
        var desktop = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainPages/RemoteDrawPage.axaml"));
        var phone = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));

        foreach (var xaml in new[] { desktop, phone })
        {
            Assert.Contains("ViewModel.ResultPlaceholderTitle", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewModel.ResultPlaceholderHint", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Resources.RD_NoResult", xaml, StringComparison.Ordinal);
        }

        // 三语齐全，且两行都**不以句末标点结尾**（用户明确要求）。
        foreach (var resource in new[] { "Resources.resx", "Resources.en-US.resx", "Resources.ja-JP.resx" })
        {
            var text = File.ReadAllText(GetRepositoryPath(Path.Combine("SecRandom/Langs/Mobile", resource)));
            Assert.Contains("RD_NoResult\"", text, StringComparison.Ordinal);
            Assert.Contains("RD_NoResult_D\"", text, StringComparison.Ordinal);

            foreach (var key in new[] { "RD_NoResult", "RD_NoResult_D" })
            {
                var value = ExtractResourceValue(text, key);
                Assert.False(
                    value.EndsWith('。') || value.EndsWith('.') || value.EndsWith('．'),
                    $"{resource} 的 {key} 不该以句末标点结尾：{value}");
            }
        }
    }

    /// <summary>从一个 resx 文件里取出某个键的取值（够用即可，不引 XML 解析）。</summary>
    private static string ExtractResourceValue(string resx, string key)
    {
        var start = resx.IndexOf($"name=\"{key}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"resx 里找不到键 {key}");

        var valueStart = resx.IndexOf("<value>", start, StringComparison.Ordinal);
        var valueEnd = resx.IndexOf("</value>", valueStart, StringComparison.Ordinal);
        Assert.True(valueStart >= 0 && valueEnd > valueStart, $"键 {key} 没有取值");

        return resx[(valueStart + "<value>".Length)..valueEnd];
    }

    [Fact]
    public void 条件UI_两个视图都按能力闸门显示且取值来自设备派生()
    {
        var desktop = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainPages/RemoteDrawPage.axaml"));
        var phone = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/Mobile/MobileRemoteDrawPage.axaml"));

        foreach (var xaml in new[] { desktop, phone })
        {
            // 条件区只在**设备声明了 draw.trigger.conditions**时显示；老设备显示一句说明，绝不假装能筛。
            Assert.Contains("IsVisible=\"{Binding ViewModel.ShowsLotteryConditions}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewModel.ShowsLotteryConditionsUnsupported", xaml, StringComparison.Ordinal);
            Assert.Contains("Resources.RD_ConditionsUnsupported", xaml, StringComparison.Ordinal);

            // 取值全部来自设备已读回来的数据，没有自由文本入口。
            Assert.Contains("ItemsSource=\"{Binding ViewModel.PrizeTags}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("ItemsSource=\"{Binding ViewModel.RecipientListNames}\"", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewModel.RecipientGenderOptions", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewModel.RecipientGroupOptions", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("Resources.RD_LotteryNoScope", xaml, StringComparison.Ordinal);
        }

        // 三语齐全（条件区标题、标签、说明、发放对象、不指定、老设备说明）。
        foreach (var resource in new[] { "Resources.resx", "Resources.en-US.resx", "Resources.ja-JP.resx" })
        {
            var text = File.ReadAllText(GetRepositoryPath(Path.Combine("SecRandom/Langs/Mobile", resource)));
            foreach (var key in new[]
                     {
                         "RD_Conditions", "RD_PrizeTags", "RD_PrizeTagsHint",
                         "RD_Recipient", "RD_RecipientNone", "RD_ConditionsUnsupported"
                     })
            {
                Assert.Contains($"name=\"{key}\"", text, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void 桌面版式_结果区只绑回执派生的成员()
    {
        var xaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainPages/RemoteDrawPage.axaml"));

        // 左侧结果区列的是 VM 里由回执填充的 DrawnMembers，空态/失败态则绑 HasResult 与 EmptyStateText，
        // 视图自己不产生成员、也不在没有回执时拼一句"抽到了某某"。
        Assert.Contains("ItemsSource=\"{Binding ViewModel.DrawnMembers}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ViewModel.HasResult}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding !ViewModel.HasResult}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ViewModel.HasEmptyState}\"", xaml, StringComparison.Ordinal);

        // VM 的 DrawnMembers 只有一个来源：回执（detail.drawn）解析出来的成员。
        var viewModelSource = File.ReadAllText(GetRepositoryPath(@"SecRandom/ViewModels/Mobile/MobileRemoteDrawViewModel.cs"));
        Assert.Contains("foreach (var member in finished.DrawnMembers())", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("DrawnMembers.Add(member);", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_开关只切换显隐而绝不增删侧栏项()
    {
        var source = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/MainView.axaml.cs"));

        // 远程抽取入口与抽奖入口走同一套 SetPageItemVisibility：只动 IsVisible。
        Assert.Contains("SetPageItemVisibility(item, RemoteDrawPageId, isRemoteDrawVisible);", source, StringComparison.Ordinal);

        // 集控状态变化 → 立刻重算显隐（不需要重启）。
        Assert.Contains("store.Changed += NodeStateOnChanged;", source, StringComparison.Ordinal);
        Assert.Contains("NodeStateStore?.Changed -= NodeStateOnChanged;", source, StringComparison.Ordinal);

        // 运行时**不许**动这两个集合：FluentAvalonia 在集合变化时会丢页脚选中态并以 null 触发 ItemInvoked。
        var handler = SectionOf(source, "private void NodeStateOnChanged", "private void SelectNavigationItem");
        Assert.DoesNotContain("NavigationViewItems.Add", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationViewItems.Remove", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("FooterMenuItemsSource", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_集控页开关只写本机状态且三语齐全()
    {
        var viewModelSource = File.ReadAllText(GetRepositoryPath(@"SecRandom/ViewModels/SettingsPages/ControlSettingsPageViewModel.cs"));
        Assert.Contains("state with { RemoteDrawPageEnabled = value }", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("RemoteDrawPageEnabled = state.RemoteDrawPageEnabled;", viewModelSource, StringComparison.Ordinal);

        var xaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/SettingsPages/General/ControlSettingsPage.axaml"));
        Assert.Contains("x:Name=\"S_RemoteDrawPage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsChecked=\"{Binding RemoteDrawPageEnabled}\"", xaml, StringComparison.Ordinal);

        // 三语都必须有：少一语的键会在那个界面语言下变成空行。
        foreach (var resource in new[] { "Resources.resx", "Resources.en-US.resx", "Resources.ja-JP.resx" })
        {
            var text = File.ReadAllText(GetRepositoryPath(Path.Combine(
                "SecRandom/Langs/SettingsPages/General/Control", resource)));
            Assert.Contains("S_RemoteDrawPage\"", text, StringComparison.Ordinal);
            Assert.Contains("S_RemoteDrawPage_D\"", text, StringComparison.Ordinal);
        }

        // 手写的 Designer 也要有对应属性，否则 XAML 的 x:Static 直接编译不过（这里只是把原因写清楚）。
        var designer = File.ReadAllText(GetRepositoryPath(
            "SecRandom/Langs/SettingsPages/General/Control/Resources.Designer.cs"));
        Assert.Contains("public static string S_RemoteDrawPage", designer, StringComparison.Ordinal);
        Assert.Contains("public static string S_RemoteDrawPage_D", designer, StringComparison.Ordinal);
    }

    [Fact]
    public void 接线_接入卡不做展开器且两个按钮互斥显隐()
    {
        var xaml = File.ReadAllText(GetRepositoryPath(@"SecRandom/Views/SettingsPages/General/ControlSettingsPage.axaml"));
        var card = SectionOf(xaml, "x:Name=\"S_NodeEnrollment\"", "</fa:FASettingsExpander>", includeStart: true);

        // 接入卡是普通设置卡，不是"展开式下拉框"：整张卡一个 item 都没有，所以 FluentAvalonia 连展开箭头都不渲染
        // （FASettingsExpander 只要有一个 item 就会画出 展开/收起 箭头并把正文区留出来）。
        Assert.DoesNotContain("IsExpanded", card, StringComparison.Ordinal);

        // 内容全在 Footer 里：Footer 之后到卡片结尾不允许再有任何元素——那正是"item"（= 展开箭头）的来源。
        const string footerOpen = "<fa:FASettingsExpander.Footer>";
        const string footerClose = "</fa:FASettingsExpander.Footer>";
        var footerStart = card.IndexOf(footerOpen, StringComparison.Ordinal);
        var footerEnd = card.IndexOf(footerClose, StringComparison.Ordinal);
        Assert.True(footerStart >= 0 && footerEnd > footerStart, "接入卡必须把内容写在 FASettingsExpander.Footer 里");
        var footer = card[footerStart..(footerEnd + footerClose.Length)];
        Assert.DoesNotContain("<", card[(footerEnd + footerClose.Length)..], StringComparison.Ordinal);

        // 失败提示跟着 Footer 一起走（卡片没有正文区，放正文区就等于把提示塞进展开器里）。
        Assert.Contains("IsVisible=\"{Binding ShowEnrollmentMessage}\"", footer, StringComparison.Ordinal);

        // 状态右对齐，接入码带上限——上限值由 VM 提供，界面不写死业务数字。
        Assert.Contains("TextAlignment=\"Right\"", card, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"{Binding EnrollmentCodeMaxLength}\"", card, StringComparison.Ordinal);

        // 一个动作格：显隐绑互补的两个属性，IsEnabled 仍分别是各自的 Can*。
        Assert.Contains("IsVisible=\"{Binding ShowEnroll}\"", card, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding ShowClearEnrollment}\"", card, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanEnroll}\"", card, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{Binding CanClearEnrollment}\"", card, StringComparison.Ordinal);

        var viewModelSource = File.ReadAllText(GetRepositoryPath(@"SecRandom/ViewModels/SettingsPages/ControlSettingsPageViewModel.cs"));

        // 互补是"这一格永远恰好有一个按钮"的唯一保证；上限是一个具名常量，不是散在 XAML 里的魔法数字。
        Assert.Contains("public bool ShowEnroll => !ShowClearEnrollment;", viewModelSource, StringComparison.Ordinal);
        Assert.Contains(
            "public bool ShowClearEnrollment => CanClearEnrollment && string.IsNullOrWhiteSpace(EnrollmentCode);",
            viewModelSource,
            StringComparison.Ordinal);
        Assert.Contains(
            "public int EnrollmentCodeMaxLength => NodeEnrollmentClient.MaxCodeLength;",
            viewModelSource,
            StringComparison.Ordinal);

        var clientSource = File.ReadAllText(GetRepositoryPath(@"SecRandom/Services/ControlNode/NodeEnrollmentClient.cs"));
        Assert.Contains("public const int MaxCodeLength", clientSource, StringComparison.Ordinal);
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
