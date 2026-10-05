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
///         <item><description>桌面：被控端 ✅，控制端页 ❌（控制端是 Web 控制台），解锁闸门 ❌</description></item>
///         <item><description>平板：被控端 ✅，控制端页 ✅（主界面侧栏），解锁闸门 ❌</description></item>
///         <item><description>手机：被控端 ❌，控制端页 ✅（底部第 5 档），解锁闸门 ✅</description></item>
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
/// </remarks>
/// <param name="IsMobile">移动平台（手机或平板）。</param>
/// <param name="UsesDesktopMainView">移动平台里用桌面主界面的那一类：平板（iPadOS / Android 平板）。</param>
/// <param name="UsesMobileShell">用 <c>MobileRootView</c> 底部导航的那一类：手机。</param>
/// <param name="RunsControlNode">本机跑集控节点（被控端）：桌面或平板。</param>
/// <param name="UsesRemoteDrawPage">本机有"远程抽取"页（控制端）：手机或平板。</param>
/// <param name="UsesUnlockedDrawGate">本机没有集控节点，只能注册解锁闸门：手机。</param>
public readonly record struct AppHostShape(
    bool IsMobile,
    bool UsesDesktopMainView,
    bool UsesMobileShell,
    bool RunsControlNode,
    bool UsesRemoteDrawPage,
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
            UsesRemoteDrawPage: isMobile,
            UsesUnlockedDrawGate: isMobile && !usesDesktopMainView);
    }
}
