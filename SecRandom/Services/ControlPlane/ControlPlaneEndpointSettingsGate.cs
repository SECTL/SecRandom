namespace SecRandom.Services.ControlPlane;

/// <summary>
///     "控制面地址设置卡"的运行时总开关。
/// </summary>
/// <remarks>
///     <para>
///         与调试页的"内幕设置"同一条逻辑：**只对本次运行有效**、刻意不落盘，重启后回到隐藏状态。
///         打开它，集控设置页（<c>settings.general.control</c>）才显示那张控制面地址设置卡。
///     </para>
///     <para>
///         为什么默认藏着：集控控制面地址是**自建部署**才需要改的东西，普通用户改了它只会把控制台
///         指向一个连不上的服务器——它不是一项日常设置，而是一枚"我知道自己在做什么"的开关。
///     </para>
/// </remarks>
public interface IControlPlaneEndpointSettingsGate
{
    bool IsRevealed { get; }

    void SetRevealed(bool revealed);

    event EventHandler? Changed;
}

/// <inheritdoc />
public sealed class ControlPlaneEndpointSettingsGate : IControlPlaneEndpointSettingsGate
{
    private readonly object _gate = new();
    private bool _isRevealed;

    public bool IsRevealed
    {
        get
        {
            lock (_gate)
            {
                return _isRevealed;
            }
        }
    }

    public event EventHandler? Changed;

    public void SetRevealed(bool revealed)
    {
        lock (_gate)
        {
            if (_isRevealed == revealed)
                return;

            _isRevealed = revealed;
        }

        // 事件在锁外触发：订阅方（设置页 ViewModel）会切到 UI 线程，回调里再进锁会自找麻烦。
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
