using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using LR = SecRandom.Langs.SettingsPages.General.Control.Resources;

namespace SecRandom.Views.ControlNode;

/// <summary>
///     集控锁屏窗：全屏拦截键鼠，直到远程解除、倒计时结束或本机收回远控权。
/// </summary>
/// <remarks>
///     <para>
///         必须**自己保证能解除**：远程控制台可能已经断线，老师不能因为一条锁屏命令被永久关在
///         自己电脑的界面上。所以这里有且只有两个本机出口：倒计时（如果控制台给了），以及
///         长按 5 秒 + 二次确认。
///     </para>
///     <para>
///         键盘一律吞掉（含 Esc、Alt+F4 能拦则拦；任务管理器那种系统级快捷键拦不住，也不该假装能拦）。
///         鼠标点击不产生任何动作，只有右下角长按区域例外。
///     </para>
/// </remarks>
public partial class ControlLockScreenWindow : Window
{
    private const int HoldSecondsRequired = 5;

    private DispatcherTimer? _autoUnlockTimer;
    private DispatcherTimer? _holdTimer;
    private int _remainingAutoSeconds;
    private int _heldSeconds;

    public ControlLockScreenWindow()
    {
        InitializeComponent();

        UnlockHintText.Text = LR.LockScreen_LongPress_Hint;
        ConfirmText.Text = LR.LockScreen_Confirm;
        ConfirmButton.Content = LR.C_Unlock;
        CancelButton.Content = LR.C_Cancel;
    }

    /// <summary>是否被本机用户确认解除。</summary>
    public bool UnlockedByUser { get; private set; }

    /// <summary>设置锁屏内容并启动可选的自动解除倒计时。必须在 UI 线程调用。</summary>
    public void Configure(string? message, int? unlockAfterSeconds)
    {
        MessageText.Text = string.IsNullOrWhiteSpace(message) ? LR.LockScreen_Message_Default : message;

        _autoUnlockTimer?.Stop();
        if (unlockAfterSeconds is { } seconds and > 0)
        {
            _remainingAutoSeconds = seconds;
            UpdateAutoCountdown();
            _autoUnlockTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnAutoUnlockTick);
            _autoUnlockTimer.Start();
        }
        else
        {
            CountdownText.IsVisible = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 不区分按键：锁屏期间本机不应通过键盘操作任何东西。
        e.Handled = true;
        base.OnKeyDown(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        e.Handled = true;
        base.OnTextInput(e);
    }

    private void OnAutoUnlockTick(object? sender, EventArgs e)
    {
        _remainingAutoSeconds--;
        if (_remainingAutoSeconds <= 0)
        {
            _autoUnlockTimer?.Stop();
            Close();
            return;
        }

        UpdateAutoCountdown();
    }

    private void UpdateAutoCountdown()
    {
        CountdownText.IsVisible = true;
        CountdownText.Text = string.Format(LR.LockScreen_AutoUnlock, _remainingAutoSeconds);
    }

    private void OnUnlockPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        _heldSeconds = 0;
        _holdTimer?.Stop();
        _holdTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnHoldTick);
        _holdTimer.Start();
        UnlockHintText.Text = string.Format(LR.LockScreen_Hold_Progress, HoldSecondsRequired);
    }

    private void OnHoldTick(object? sender, EventArgs e)
    {
        _heldSeconds++;
        var remaining = HoldSecondsRequired - _heldSeconds;
        if (remaining <= 0)
        {
            _holdTimer?.Stop();
            _holdTimer = null;
            ConfirmPanel.IsVisible = true;
            return;
        }

        UnlockHintText.Text = string.Format(LR.LockScreen_Hold_Progress, remaining);
    }

    private void OnUnlockPointerReleased(object? sender, PointerReleasedEventArgs e) => CancelHold();

    private void OnUnlockPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => CancelHold();

    private void CancelHold()
    {
        _holdTimer?.Stop();
        _holdTimer = null;
        UnlockHintText.Text = LR.LockScreen_LongPress_Hint;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        UnlockedByUser = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => ConfirmPanel.IsVisible = false;

    protected override void OnClosed(EventArgs e)
    {
        _autoUnlockTimer?.Stop();
        _holdTimer?.Stop();
        _autoUnlockTimer = null;
        _holdTimer = null;
        base.OnClosed(e);
    }
}