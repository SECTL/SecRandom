using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SecRandom.Core.Views;
using SecRandom.ViewModels.MainPages;
using SecRandom.Views.MainPages;

namespace SecRandom.Services.ViewEngine;

public sealed class TimerViewService : IDisposable
{
    internal const string ViewId = "main.timer";

    private readonly IViewEngine _viewEngine;
    private readonly IViewHostProvider _hostProvider;
    private readonly TimerViewModel _viewModel;
    private TimerMiniWindow? _miniWindow;
    private IViewHandle? _fullViewHandle;
    private Window? _fullViewWindow;
    private WindowState _fullScreenPreviousState = WindowState.Normal;
    private bool _pendingFullScreen;
    private bool _changingFullScreen;

    public TimerViewService(IViewEngine viewEngine, IViewHostProvider hostProvider, TimerViewModel viewModel)
    {
        _viewEngine = viewEngine;
        _hostProvider = hostProvider;
        _viewModel = viewModel;
        // 计时走过阈值时自动缩成小窗：只有这里认识窗口，ViewModel 只发一个"该缩了"的信号
        _viewModel.AutoMiniWindowRequested += OnAutoMiniWindowRequested;
    }

    public Task ShowAsync(CancellationToken cancellationToken = default) => ShowFullViewAsync(cancellationToken);

    public void ShowMiniWindow()
    {
        if (_miniWindow is { IsVisible: true })
        {
            _miniWindow.Activate();
            return;
        }

        App.NotifyTimerSurfaceEntered();
        _miniWindow = new TimerMiniWindow(_viewModel, RestoreFullWindow, EnterFullScreenFromMini);
        _miniWindow.Closed += (_, _) => _miniWindow = null;
        _miniWindow.Show();

        // The mini window replaces the full host. Close it only after the mini window is visible,
        // otherwise Avalonia may treat the full host as the last application window.
        if (Volatile.Read(ref _fullViewHandle) is { } fullViewHandle)
            _ = fullViewHandle.CloseAsync(ViewCloseReason.Programmatic);
    }

    public void RestoreFullWindow()
    {
        RestoreFullWindow(enterFullScreen: false);
    }

    private void RestoreFullWindow(bool enterFullScreen)
    {
        _pendingFullScreen = enterFullScreen;
        if (!enterFullScreen)
            App.NotifyTimerSurfaceExited();

        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
        _ = ShowAsync();
    }

    public void ToggleFullScreen(Window window)
    {
        if (_viewModel.IsFullScreen)
            ExitFullScreen(window);
        else
            EnterFullScreen(window);
    }

    public void AttachFullViewWindow(Window window)
    {
        if (!ReferenceEquals(_fullViewWindow, window))
            window.Height = 700;

        if (_fullViewWindow is not null && !ReferenceEquals(_fullViewWindow, window))
            _fullViewWindow.PropertyChanged -= FullViewWindowOnPropertyChanged;

        _fullViewWindow = window;
        window.PropertyChanged -= FullViewWindowOnPropertyChanged;
        window.PropertyChanged += FullViewWindowOnPropertyChanged;
        if (_pendingFullScreen)
        {
            _pendingFullScreen = false;
            EnterFullScreen(window);
        }
    }

    private void EnterFullScreenFromMini() => RestoreFullWindow(enterFullScreen: true);

    private void EnterFullScreen(Window window)
    {
        if (_viewModel.IsFullScreen || _changingFullScreen)
            return;

        _changingFullScreen = true;
        try
        {
            App.NotifyTimerSurfaceEntered();
            _fullScreenPreviousState = window.WindowState == WindowState.FullScreen
                ? WindowState.Normal
                : window.WindowState;
            _fullViewWindow = window;
            window.WindowState = WindowState.FullScreen;
            _viewModel.SetFullScreenState(true);
        }
        finally
        {
            _changingFullScreen = false;
        }
    }

    private void ExitFullScreen(Window? window = null)
    {
        if (!_viewModel.IsFullScreen || _changingFullScreen)
            return;

        _changingFullScreen = true;
        try
        {
            var target = _fullViewWindow ?? window;
            if (target is not null && target.WindowState == WindowState.FullScreen)
                target.WindowState = _fullScreenPreviousState;
            _viewModel.SetFullScreenState(false);
            _fullViewWindow = target;
            App.NotifyTimerSurfaceExited();
        }
        finally
        {
            _changingFullScreen = false;
        }
    }

    public void Dispose()
    {
        _viewModel.AutoMiniWindowRequested -= OnAutoMiniWindowRequested;
        _pendingFullScreen = false;
        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
        if (_fullViewWindow is { } fullViewWindow)
            fullViewWindow.PropertyChanged -= FullViewWindowOnPropertyChanged;
        _fullViewWindow = null;
        _viewModel.SetFullScreenState(false);
        App.NotifyTimerSurfaceExited();
        _viewModel.Dispose();
    }

    private async Task ShowFullViewAsync(CancellationToken cancellationToken)
    {
        var handle = await _viewEngine.ShowAsync(
            ViewId,
            _hostProvider is DesktopViewHostProvider
                ? new ViewShowOptions { ActivationPreference = ViewActivationPreference.NewHost }
                : null,
            cancellationToken);

        _fullViewHandle = handle;
        // 大窗被关掉（用户关闭或自动缩小）之后句柄就不再代表一个开着的大窗。
        // 续接跑在线程池上：它只清一个字段，不碰任何 UI。
        _ = handle.Completion.ContinueWith(
            _ => Dispatcher.UIThread.Post(() => OnFullViewCompleted(handle)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    ///     自动缩小：先开小窗，再关大窗。
    /// </summary>
    /// <remarks>
    ///     顺序不能反：应用是"最后一个窗口关掉就退出"，先关大窗会在小窗出现之前把应用整个关掉。
    ///     大窗本来就没开时什么都不做——用户可能只留着小窗。
    /// </remarks>
    private void OnAutoMiniWindowRequested()
    {
        var handle = Volatile.Read(ref _fullViewHandle);
        if (handle is null)
            return;

        ShowMiniWindow();
    }

    private void OnFullViewCompleted(IViewHandle handle)
    {
        if (!ReferenceEquals(Volatile.Read(ref _fullViewHandle), handle))
            return;

        _fullViewHandle = null;
        if (_fullViewWindow is { } previousWindow)
            previousWindow.PropertyChanged -= FullViewWindowOnPropertyChanged;
        _fullViewWindow = null;
        _viewModel.SetFullScreenState(false);
        if (_miniWindow is null)
            App.NotifyTimerSurfaceExited();
    }

    private void FullViewWindowOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is not Window window
            || e.Property != Window.WindowStateProperty
            || !_viewModel.IsFullScreen
            || _changingFullScreen
            || window.WindowState == WindowState.FullScreen)
            return;

        ExitFullScreen(window);
    }
}
