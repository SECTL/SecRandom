using Avalonia.Controls;
using SecRandom.Core.Abstraction.Services.Views;

namespace SecRandom.Core.Services.Views;

/// <summary>
///     跨 shell 的弹层：桌面有叠加层就用叠加层，没有（移动端/无壳）就退回独立窗口。
///     插件只写一次，不必判断自己在哪个 shell 上跑。
/// </summary>
public sealed class PopupService : IPopupService
{
    private readonly IOverlayHostService? _overlayHost;
    private readonly IWindowService? _windowService;
    private readonly Dictionary<string, bool> _open = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public PopupService(IOverlayHostService? overlayHost = null, IWindowService? windowService = null)
    {
        _overlayHost = overlayHost;
        _windowService = windowService;

        if (_overlayHost is not null)
            _overlayHost.OverlayClosed += OnOverlayClosed;
        if (_windowService is not null)
            _windowService.WindowClosed += OnWindowClosed;
    }

    /// <inheritdoc />
    public event EventHandler<PopupClosedEventArgs>? PopupClosed;

    /// <inheritdoc />
    public bool IsSupported => _overlayHost?.IsSupported == true || _windowService?.IsSupported == true;

    /// <inheritdoc />
    public IReadOnlyList<string> OpenPopupIds
    {
        get
        {
            lock (_gate)
                return _open.Keys.ToArray();
        }
    }

    /// <inheritdoc />
    public PopupHandle Show(PopupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var requestedId = string.IsNullOrWhiteSpace(options.Id)
            ? $"plugin.popup.{Guid.NewGuid():N}"
            : options.Id!;

        if (!options.PreferSeparateWindow && _overlayHost?.IsSupported == true)
        {
            var id = _overlayHost.Show(new OverlayOptions
            {
                Id = requestedId,
                Content = options.Content,
                ShowBackdrop = options.ShowBackdrop,
                BackdropColor = options.BackdropColor,
                DismissOnClickOutside = options.DismissOnClickOutside,
                DismissOnEscape = options.DismissOnEscape,
                BlockInput = options.BlockInput
            });

            lock (_gate)
                _open[id] = false;
            return new PopupHandle(id, false);
        }

        if (_windowService?.IsSupported == true)
        {
            var id = _windowService.Show(new WindowRequest
            {
                Id = requestedId,
                Content = options.Content,
                ShowTitleBar = false,
                CanResize = false,
                CloseOnEscape = options.DismissOnEscape,
                CenterOnScreen = true
            });

            lock (_gate)
                _open[id] = true;
            return new PopupHandle(id, true);
        }

        // 两条路都不支持：不抛异常，返回句柄让调用方自己决定（关掉时是空操作）。
        return new PopupHandle(requestedId, false);
    }

    /// <inheritdoc />
    public void Close(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        bool usedWindow;
        lock (_gate)
        {
            if (!_open.TryGetValue(id, out usedWindow))
                usedWindow = false;
        }

        if (usedWindow)
            _windowService?.Close(id);
        else
            _overlayHost?.Close(id);
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        string[] ids;
        lock (_gate)
            ids = _open.Keys.ToArray();

        foreach (var id in ids)
            Close(id);
    }

    private void OnOverlayClosed(object? sender, OverlayClosedEventArgs e)
    {
        if (!Remove(e.Id))
            return;

        PopupClosed?.Invoke(this, new PopupClosedEventArgs(e.Id, e.ClosedByUser));
    }

    private void OnWindowClosed(object? sender, WindowClosedEventArgs e)
    {
        if (!Remove(e.Id))
            return;

        PopupClosed?.Invoke(this, new PopupClosedEventArgs(e.Id, e.ClosedByUser));
    }

    private bool Remove(string id)
    {
        lock (_gate)
            return _open.Remove(id);
    }
}
