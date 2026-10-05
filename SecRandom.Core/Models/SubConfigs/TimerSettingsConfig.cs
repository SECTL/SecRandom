using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     计时器设置：计时进行到一定时长之后，自动从大窗缩成小窗。
/// </summary>
/// <remarks>
///     <para>
///         阈值按"**计时开始之后走过了多久**"算，不是"还剩多少"。两种口径看起来只差一个减法，
///         用起来却相反：按剩余时间算，长倒计时会在一开始就缩下去；按已走过的时间算，
///         起点是"老师按下开始的那一刻"，与这次要计多久无关。
///     </para>
///     <para>
///         这里的规则是纯函数，只回答"这一刻该不该缩"，窗口怎么切换由应用层的
///         <c>TimerViewService</c> 决定——Core 不认识窗口。
///     </para>
/// </remarks>
public partial class TimerSettingsConfig : ObservableObject
{
    /// <summary>自动缩小阈值的下限（秒）：0 秒等于"一开始就缩"，那不是一个能用的设置。</summary>
    public const int MinAutoMiniWindowSeconds = 1;

    /// <summary>自动缩小阈值的上限（秒，一小时）：再长就只有"别开这个功能"一个意思了。</summary>
    public const int MaxAutoMiniWindowSeconds = 3600;

    [ObservableProperty] private bool _autoMiniWindowEnabled = true;
    [ObservableProperty] private int _autoMiniWindowAfterSeconds = 10;

    /// <summary>实际生效的阈值秒数：越界的旧文件值或远程写入都被夹回合法区间。</summary>
    public int EffectiveAutoMiniWindowSeconds =>
        Math.Clamp(AutoMiniWindowAfterSeconds, MinAutoMiniWindowSeconds, MaxAutoMiniWindowSeconds);

    /// <summary>
    ///     这一次计时是不是该缩成小窗了。
    /// </summary>
    /// <param name="isRunning">计时是否在走：倒计时与秒表都算，时钟不参与。</param>
    /// <param name="runningSeconds">本次计时已经走过的秒数（暂停的时间不计入）。</param>
    /// <param name="alreadyTriggered">
    ///     本次计时是否已经缩过一次。一次计时只缩一次：用户把大窗留下来（关掉小窗就还原大窗）之后，
    ///     每 33ms 的刷新都再缩一次，会把窗口变成一个关不掉的弹窗。
    /// </param>
    public bool ShouldShrinkToMiniWindow(bool isRunning, double runningSeconds, bool alreadyTriggered) =>
        AutoMiniWindowEnabled && isRunning && !alreadyTriggered &&
        runningSeconds >= EffectiveAutoMiniWindowSeconds;
}
