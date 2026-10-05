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

        _miniWindow = new TimerMiniWindow(_viewModel, RestoreFullWindow);
        _miniWindow.Closed += (_, _) => _miniWindow = null;
        _miniWindow.Show();
    }

    public void RestoreFullWindow()
    {
        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
        _ = ShowAsync();
    }

    public void Dispose()
    {
        _viewModel.AutoMiniWindowRequested -= OnAutoMiniWindowRequested;
        _miniWindow?.AllowClose();
        _miniWindow?.Close();
        _miniWindow = null;
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
            _ => Interlocked.CompareExchange(ref _fullViewHandle, null, handle),
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
        _ = handle.CloseAsync(ViewCloseReason.Programmatic);
    }
}
