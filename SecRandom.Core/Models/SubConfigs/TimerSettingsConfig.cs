using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     计时器设置：在计时器页面上**一段时间没有任何操作**之后，自动从大窗缩成小窗。
/// </summary>
/// <remarks>
///     <para>
///         观察的是"人还有没有在动这个页面"，不是"计时走了多久"。老师按下开始、把时间留在屏幕上
///         不再碰它，到设定时长就该缩成小窗；反过来，正在调时长、切模式、点按钮时不该突然缩下去，
///         所以任何一次操作都会把这段"无操作时间"清零重算。
///     </para>
///     <para>
///         三种模式各有开关：倒计时默认开（"开始计时后专心做别的事"的主场景），秒表与时钟默认关
///         （它们往往就是要摆在屏幕上看着，默认缩下去等于替用户做了决定）。
///     </para>
///     <para>
///         这里的规则是纯函数，只回答"这一刻该不该缩"；哪种模式、当前模式开没开、离开上一次操作
///         过了多久由应用层 <c>TimerViewModel</c> 给出——Core 既不认识窗口，也不认识输入事件。
///     </para>
/// </remarks>
public partial class TimerSettingsConfig : ObservableObject
{
    /// <summary>自动缩小阈值的下限（秒）：0 秒等于"一打开就缩"，那不是一个能用的设置。</summary>
    public const int MinAutoMiniWindowSeconds = 1;

    /// <summary>自动缩小阈值的上限（秒，一小时）：再长就只有"别开这个功能"一个意思了。</summary>
    public const int MaxAutoMiniWindowSeconds = 3600;

    /// <summary>倒计时：默认开启。</summary>
    [ObservableProperty] private bool _autoMiniWindowCountdownEnabled = true;

    /// <summary>秒表：默认关闭——秒表通常就是要看大屏的累计时间。</summary>
    [ObservableProperty] private bool _autoMiniWindowStopwatchEnabled;

    /// <summary>时钟：默认关闭——它没有开始/暂停，缩小只是把同一块表挪进小窗。</summary>
    [ObservableProperty] private bool _autoMiniWindowClockEnabled;

    [ObservableProperty] private int _autoMiniWindowAfterSeconds = 10;

    /// <summary>实际生效的阈值秒数：越界的旧文件值或远程写入都被夹回合法区间。</summary>
    public int EffectiveAutoMiniWindowSeconds =>
        Math.Clamp(AutoMiniWindowAfterSeconds, MinAutoMiniWindowSeconds, MaxAutoMiniWindowSeconds);

    /// <summary>
    ///     这一刻该不该缩成小窗。
    /// </summary>
    /// <param name="enabledForCurrentMode">当前模式的开关（倒计时 / 秒表 / 时钟 各一个）。</param>
    /// <param name="idleSeconds">离开上一次操作已经过了多少秒（打开页面本身算一次操作）。</param>
    /// <param name="alreadyTriggered">
    ///     这一段无操作时间是否已经缩过一次。同一次无操作只缩一次：用户把小窗关掉会还原大窗，
    ///     这里要是还缩，每 33ms 的刷新都会再缩一次，大窗就再也留不住了——下一次操作才重新开始算。
    /// </param>
    public bool ShouldShrinkToMiniWindow(bool enabledForCurrentMode, double idleSeconds, bool alreadyTriggered) =>
        enabledForCurrentMode && !alreadyTriggered && idleSeconds >= EffectiveAutoMiniWindowSeconds;
}
