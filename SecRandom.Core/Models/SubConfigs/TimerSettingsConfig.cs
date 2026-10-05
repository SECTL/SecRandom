using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     计时器设置：计时（或时钟显示）走到一定时长之后，自动从大窗缩成小窗。
/// </summary>
/// <remarks>
///     <para>
///         阈值按"**开始之后走过了多久**"算，不是"还剩多少"。两种口径看起来只差一个减法，
///         用起来却相反：按剩余时间算，长倒计时会在一开始就缩下去；按已走过的时间算，
///         起点是"老师按下开始的那一刻"，与这次要计多久无关。时钟没有"开始"这个动作，
///         它的"走过多久"就是**已经显示了多久**。
///     </para>
///     <para>
///         三种模式各有开关：倒计时默认开（"快到点前盯小窗"的主场景），秒表与时钟默认关
///         （它们往往就是要看大屏，默认缩下去等于替用户做了决定）。
///     </para>
///     <para>
///         这里的规则是纯函数，只回答"这一刻该不该缩"，哪种模式用哪个开关、走了多久由应用层
///         <c>TimerViewModel</c> 给出——Core 既不认识窗口，也不认识计时状态。
///     </para>
/// </remarks>
public partial class TimerSettingsConfig : ObservableObject
{
    /// <summary>自动缩小阈值的下限（秒）：0 秒等于"一开始就缩"，那不是一个能用的设置。</summary>
    public const int MinAutoMiniWindowSeconds = 1;

    /// <summary>自动缩小阈值的上限（秒，一小时）：再长就只有"别开这个功能"一个意思了。</summary>
    public const int MaxAutoMiniWindowSeconds = 3600;

    /// <summary>倒计时：默认开启。</summary>
    [ObservableProperty] private bool _autoMiniWindowCountdownEnabled = true;

    /// <summary>秒表：默认关闭——秒表通常就是要看大屏的累计时间。</summary>
    [ObservableProperty] private bool _autoMiniWindowStopwatchEnabled;

    /// <summary>时钟：默认关闭——它没有"开始"，缩小只是把同一块表挪进小窗。</summary>
    [ObservableProperty] private bool _autoMiniWindowClockEnabled;

    [ObservableProperty] private int _autoMiniWindowAfterSeconds = 10;

    /// <summary>实际生效的阈值秒数：越界的旧文件值或远程写入都被夹回合法区间。</summary>
    public int EffectiveAutoMiniWindowSeconds =>
        Math.Clamp(AutoMiniWindowAfterSeconds, MinAutoMiniWindowSeconds, MaxAutoMiniWindowSeconds);

    /// <summary>
    ///     这一刻该不该缩成小窗。
    /// </summary>
    /// <param name="enabledForCurrentMode">当前模式的开关（倒计时 / 秒表 / 时钟 各一个）。</param>
    /// <param name="elapsedSeconds">当前模式已经走过（或已经显示）的秒数。</param>
    /// <param name="alreadyTriggered">
    ///     这一次会话是否已经缩过一次。一次会话只缩一次：用户把大窗留下来（关掉小窗就还原大窗）之后，
    ///     每 33ms 的刷新都再缩一次，会把窗口变成一个关不掉的弹窗。
    /// </param>
    public bool ShouldShrinkToMiniWindow(bool enabledForCurrentMode, double elapsedSeconds, bool alreadyTriggered) =>
        enabledForCurrentMode && !alreadyTriggered && elapsedSeconds >= EffectiveAutoMiniWindowSeconds;
}
