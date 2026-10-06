using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Abstraction.Services.Notifications;
using SecRandom.Core.Abstraction.Services.Threading;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Services.Notifications;

/// <summary>
///     全局忙碌指示：插件用它显示"正在处理"的遮罩与进度，不用自己造窗口。
///     <para>
///         实现挂在 <see cref="IOverlayHostService" /> 上（浮动窗口用固定 id，只关自己那一个，
///         不会误关别的插件叠加上来的界面）。宿主没有叠加层时（移动端/无壳）退化为空操作，
///         <see cref="IsBusy" /> 仍然可用，插件逻辑不受影响。
///     </para>
/// </summary>
public sealed class BusyIndicator : IBusyIndicator
{
    private const string OverlayId = "host.busy.indicator";

    private readonly IOverlayHostService? _overlayHost;
    private readonly IUiScheduler? _scheduler;
    private readonly Lock _gate = new();

    private int _scopeCount;
    private string? _overlayId;
    private TextBlock? _messageBlock;
    private ProgressBar? _progressBar;

    public BusyIndicator(IOverlayHostService? overlayHost = null, IUiScheduler? scheduler = null)
    {
        _overlayHost = overlayHost;
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public bool IsBusy
    {
        get
        {
            lock (_gate)
                return _scopeCount > 0;
        }
    }

    /// <inheritdoc />
    public IDisposable Begin(string? message = null, double? progress = null)
    {
        lock (_gate)
            _scopeCount++;

        ShowOrUpdate(message, progress);
        return new Scope(this);
    }

    /// <inheritdoc />
    public void Report(string? message, double? progress = null)
    {
        lock (_gate)
        {
            if (_scopeCount == 0 && _overlayId is null)
                return;
        }

        ShowOrUpdate(message, progress);
    }

    private void ShowOrUpdate(string? message, double? progress)
    {
        if (_overlayHost?.IsSupported != true)
            return;

        RunOnUiThread(() =>
        {
            var overlayId = _overlayId;
            if (overlayId is null || _messageBlock is null || _progressBar is null)
            {
                BuildAndShow(message, progress);
                return;
            }

            ApplyContent(message, progress);
        });
    }

    private void BuildAndShow(string? message, double? progress)
    {
        if (_overlayHost is null)
            return;

        _messageBlock = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 320,
            FontSize = 14
        };

        _progressBar = new ProgressBar
        {
            Width = 180,
            Height = 4,
            Margin = new Thickness(0, 12, 0, 0)
        };

        ApplyContent(message, progress);

        var panel = new Border
        {
            Padding = new Thickness(24, 20, 24, 20),
            CornerRadius = new CornerRadius(8),
            Background = ResolveCardBrush(),
            BorderThickness = new Thickness(1),
            BorderBrush = ResolveBorderBrush(),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { _messageBlock, _progressBar }
            }
        };

        _overlayId = _overlayHost.Show(new OverlayOptions
        {
            Id = OverlayId,
            Content = panel,
            ShowBackdrop = false,
            DismissOnClickOutside = false,
            DismissOnEscape = false,
            BlockInput = true
        });
    }

    private void ApplyContent(string? message, double? progress)
    {
        if (_messageBlock is not null)
        {
            _messageBlock.Text = message ?? string.Empty;
            _messageBlock.IsVisible = !string.IsNullOrEmpty(message);
        }

        if (_progressBar is null)
            return;

        var hasProgress = progress is not null;
        _progressBar.IsVisible = hasProgress;
        if (!hasProgress)
            return;

        _progressBar.IsIndeterminate = false;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1;
        _progressBar.Value = Math.Clamp(progress!.Value, 0d, 1d);
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_scopeCount > 0)
                _scopeCount--;

            if (_scopeCount > 0)
                return;
        }

        var overlayId = _overlayId;
        _overlayId = null;
        _messageBlock = null;
        _progressBar = null;

        if (overlayId is not null)
            RunOnUiThread(() => _overlayHost?.Close(overlayId));
    }

    private void RunOnUiThread(Action action)
    {
        if (_scheduler is not null)
        {
            _scheduler.Post(action);
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private static IBrush ResolveCardBrush()
    {
        if (Application.Current?.TryFindResource("CardBackgroundFillColorDefaultBrush", out var value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromArgb(0xF0, 0x2B, 0x2B, 0x2B));
    }

    private static IBrush ResolveBorderBrush()
    {
        if (Application.Current?.TryFindResource("CardStrokeColorDefaultBrush", out var value) == true
            && value is IBrush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    }

    private sealed class Scope(BusyIndicator owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            owner.Release();
        }
    }
}
