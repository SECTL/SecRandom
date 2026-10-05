namespace SecRandom.Mobile;

/// <summary>
///     这台机器的宿主形态：它同时是"控制端"和"被控端"里的哪些角色。
/// </summary>
/// <remarks>
///     <para>
///         判定只有两个输入：**是不是移动平台**（<c>PlatformStartupContext.Current</c> 是不是
///         <see cref="MobilePlatformServiceRoot" />）、以及**平板是不是在单窗口宿主里用桌面主界面**
///         （<see cref="MobilePlatformServiceRoot.UsesDesktopMainView" />）。
///         其余全部由这两个值派生，并且**只有这一处派生**：把"平板算不算控制端/被控端"的判断散在
///         <c>BuildHost</c> 的十几个 <c>if</c> 里，下一次加平台时必然漏掉一条。
///     </para>
///     <list type="table">
///         <item><description>桌面：被控端 ✅，远程抽取页 ✅（主界面侧栏，**默认隐藏**，用户在集控页打开），解锁闸门 ❌</description></item>
///         <item><description>平板：被控端 ✅，远程抽取页 ✅（主界面侧栏，始终显示），解锁闸门 ❌</description></item>
///         <item><description>手机：被控端 ❌，远程抽取页 ✅（底部第 5 档，始终显示），解锁闸门 ✅</description></item>
///     </list>
///     <para>
///         <b>闸门为什么必须按宿主选</b>：<c>IControlDrawGate</c> 被 <c>LinkageDrawCoordinator</c> 在**每个宿主**上消费，
///         因此它永远要能解析——缺了它整台 Host 都起不来。但它表达的是"本机的集控是否锁定抽取"，
///         只有真的跑集控节点的宿主才有资格回答：
///     </para>
///     <list type="bullet">
///         <item><description>有节点的宿主（桌面/平板）注册真的 <c>ControlDrawGateService</c>；</description></item>
///         <item><description>没有节点的宿主（手机）只能注册中性的 <c>UnlockedControlDrawGate</c>——
///             它只是"本机没有集控"这个事实的占位符；</description></item>
///         <item><description><b>反过来都不行</b>：让平板用解锁闸门＝平板上的远程锁定失效、本地抽取被静默放行；
///             让手机用真闸门＝手机凭空多出一个它控制不了的锁。</description></item>
///     </list>
///     <para>
///         这两个标志**互斥**（<see cref="RunsControlNode" /> 与 <see cref="UsesUnlockedDrawGate" /> 不会同时为真），
///         测试里有一条断言钉住它：闸门的选择不是"两个 if 恰好写反了"。
///     </para>
///     <para>
///         <b>远程抽取页为什么三个宿主都注册同一份</b>：它就是"往别台设备下发 draw.trigger"的那一页，
///         与谁被控无关（手机只当控制台、桌面/平板两种角色都当）。三个宿主各写一份 UI/VM，
///         意味着协议一改要改三处、且必然有一处先烂掉；注册方式不同只是**外壳**差异——
///         桌面/平板没有底部导航栏，所以它是 <c>MainView</c> 的侧栏主页面；
///         手机是 <c>MobileRootView</c> 的底部第 5 档。
///     </para>
/// </remarks>
/// <param name="IsMobile">移动平台（手机或平板）。</param>
/// <param name="UsesDesktopMainView">移动平台里用桌面主界面的那一类：平板（iPadOS / Android 平板）。</param>
/// <param name="UsesMobileShell">用 <c>MobileRootView</c> 底部导航的那一类：手机。</param>
/// <param name="RunsControlNode">本机跑集控节点（被控端）：桌面或平板。</param>
/// <param name="UsesRemoteDrawSidebarPage">远程抽取页作为 <c>MainView</c> 侧栏主页面注册：桌面或平板。</param>
/// <param name="UsesRemoteDrawBottomTab">远程抽取页作为底部第 5 档 keyed 页面注册：手机。</param>
/// <param name="RemoteDrawEntryHiddenByDefault">远程抽取入口默认隐藏、需用户在本机集控页打开：**只有桌面**。</param>
/// <param name="UsesUnlockedDrawGate">本机没有集控节点，只能注册解锁闸门：手机。</param>
public readonly record struct AppHostShape(
    bool IsMobile,
    bool UsesDesktopMainView,
    bool UsesMobileShell,
    bool RunsControlNode,
    bool UsesRemoteDrawSidebarPage,
    bool UsesRemoteDrawBottomTab,
    bool RemoteDrawEntryHiddenByDefault,
    bool UsesUnlockedDrawGate)
{
    /// <summary>把"是不是移动平台 + 平板是否用桌面主界面"折算成一份宿主形态。</summary>
    public static AppHostShape Resolve(bool isMobile, bool usesDesktopMainView)
    {
        // 非移动平台上 UsesDesktopMainView 没有意义，但调用方可能从一个尚未初始化的平台根里读到 true，
        // 因此这里一律以 isMobile 为准，避免"桌面被当成平板"。
        var tablet = isMobile && usesDesktopMainView;

        return new AppHostShape(
            IsMobile: isMobile,
            UsesDesktopMainView: tablet,
            UsesMobileShell: isMobile && !usesDesktopMainView,
            RunsControlNode: !isMobile || tablet,
            UsesRemoteDrawSidebarPage: !isMobile || tablet,
            UsesRemoteDrawBottomTab: isMobile && !usesDesktopMainView,
            RemoteDrawEntryHiddenByDefault: !isMobile,
            UsesUnlockedDrawGate: isMobile && !usesDesktopMainView);
    }

    /// <summary>
    ///     远程抽取入口在这台机器上是否可见。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         纯函数，故意放在这里而不是散进 <c>MainView</c>：这是一条**产品规则**（谁默认藏、谁的开关说了算），
    ///         值得被单测而不是只活在 XAML 的行为里。
    ///     </para>
    ///     <para>
    ///         只有桌面受开关约束：平板在教室里就是控制台，把它也藏起来会让升级后的平板突然少一页；
    ///         手机压根没有这个开关（没有本地节点状态存储），按"非桌面"这一支永远显示。
    ///     </para>
    /// </remarks>
    /// <param name="isDesktop">本机是不是桌面宿主。</param>
    /// <param name="enabledByUser">用户在集控页打开的开关值（仅桌面有意义）。</param>
    public static bool IsRemoteDrawEntryVisible(bool isDesktop, bool enabledByUser) =>
        !isDesktop || enabledByUser;

    /// <summary>本机形态下的同一判定，供宿主自己按 <paramref name="enabledByUser" /> 求值。</summary>
    public bool IsRemoteDrawEntryVisible(bool enabledByUser) =>
        IsRemoteDrawEntryVisible(!IsMobile, enabledByUser);
}
