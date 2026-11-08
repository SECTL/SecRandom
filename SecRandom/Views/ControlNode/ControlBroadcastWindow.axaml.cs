using Avalonia.Controls;
using Avalonia.Threading;
using LR = SecRandom.Langs.SettingsPages.General.Control.Resources;

namespace SecRandom.Views.ControlNode;

/// <summary>
///     集控广播窗：显示一段远程下发的文本，倒计时结束后自动关闭。
/// </summary>
/// <remarks>
///     <para>
///         刻意<b>不激活窗口</b>（<c>ShowActivated="False"</c>）：抽取进行中广播一条通知，
///         不该把焦点从讲台上的抽取窗口抢走——抢走焦点会让键盘快捷键与动画节奏一起断掉。
///         但窗口仍置顶并全屏，学生看得见。
///     </para>
///     <para>
///         窗口本身不碰任何协议：文本长度、秒数在上层已由 <c>ControlBroadcastRequest</c> 校验。
///     </para>
/// </remarks>
public partial class ControlBroadcastWindow : Window
{
    private DispatcherTimer? _timer;
    private int _remainingSeconds;

    public ControlBroadcastWindow()
    {
        InitializeComponent();
    }

    /// <summary>设置内容并启动倒计时。必须在 UI 线程调用。</summary>
    public void Configure(string text, int seconds)
    {
        TitleText.Text = LR.Broadcast_Title;
        MessageText.Text = text;
        _remainingSeconds = Math.Max(1, seconds);
        UpdateCountdown();

        _timer?.Stop();
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTick);
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _remainingSeconds--;
        if (_remainingSeconds <= 0)
        {
            _timer?.Stop();
            Close();
            return;
        }

        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        CountdownText.Text = string.Format(LR.Broadcast_Countdown, _remainingSeconds);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnClosed(e);
    }
}