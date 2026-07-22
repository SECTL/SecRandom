using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace SecRandom.Core.Views;

public class ViewHostControl : UserControl, IViewHost
{
    private readonly Grid _root;
    private readonly NavigationPage _navigationPage;
    private readonly Border _modalOverlay;
    private readonly ContentControl _modalPresenter;
    private readonly List<ViewBase> _pageStack = [];
    private readonly List<ViewBase> _modalStack = [];
    private bool _isDestroyed;

    public ViewHostControl(string hostId)
    {
        HostId = hostId;
        _navigationPage = new NavigationPage();
        _modalPresenter = new ContentControl
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch
        };
        _modalOverlay = new Border
        {
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(96, 0, 0, 0)),
            Child = _modalPresenter
        };
        _root = new Grid();
        _root.Children.Add(_navigationPage);
        _root.Children.Add(_modalOverlay);
        Content = _root;
        IsVisible = false;
        IsHitTestVisible = false;
    }

    public string HostId { get; }
    public IReadOnlyList<ViewBase> PageStack => _pageStack;
    public ViewBase? ActiveModalView => _modalStack.LastOrDefault();
    public bool HasActiveView => ActiveModalView is not null || _pageStack.Count != 0;
    public event EventHandler? Destroyed;

    public async Task ShowPageAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        await RunOnUiThreadAsync(async () =>
        {
            ThrowIfDestroyed();
            var isFirstPage = _pageStack.Count == 0;
            _pageStack.Add(view);
            if (isFirstPage)
                await _navigationPage.ReplaceAsync(view).ConfigureAwait(true);
            else
                await _navigationPage.PushAsync(view).ConfigureAwait(true);
            UpdateVisibility();
        }).ConfigureAwait(false);
    }

    public Task ShowModalAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RunOnUiThreadAsync(() =>
        {
            ThrowIfDestroyed();
            _modalStack.Add(view);
            _modalPresenter.Content = view;
            _modalOverlay.IsVisible = true;
            UpdateVisibility();
        });
    }

    public Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        return RunOnUiThreadAsync(() =>
        {
            ThrowIfDestroyed();
            if (_modalStack.Contains(view))
            {
                _modalStack.Remove(view);
                _modalStack.Add(view);
                _modalPresenter.Content = view;
                _modalOverlay.IsVisible = true;
                UpdateVisibility();
                return;
            }

            if (!_pageStack.Contains(view))
                throw new InvalidOperationException("The view is not active in this host.");

            UpdateVisibility();
        });
    }

    public async Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(view);
        await RunOnUiThreadAsync(async () =>
        {
            if (_modalStack.Remove(view))
            {
                _modalPresenter.Content = ActiveModalView;
                _modalOverlay.IsVisible = ActiveModalView is not null;
                UpdateVisibility();
                return;
            }

            if (_pageStack.Remove(view))
            {
                if (ReferenceEquals(_navigationPage.CurrentPage, view))
                {
                    if (_pageStack.Count == 0)
                        await _navigationPage.ReplaceAsync(new ContentPage()).ConfigureAwait(true);
                    else
                        await _navigationPage.PopAsync().ConfigureAwait(true);
                }
                UpdateVisibility();
            }
        }).ConfigureAwait(false);
    }

    public Task<bool> RequestBackAsync(CancellationToken cancellationToken = default) => CloseActiveViewAsync(cancellationToken);

    public async Task<bool> CloseActiveViewAsync(CancellationToken cancellationToken = default)
    {
        ViewBase? activeView = null;
        await RunOnUiThreadAsync(() =>
        {
            if (_isDestroyed)
                return;

            activeView = ActiveModalView ?? _pageStack.LastOrDefault();
        }).ConfigureAwait(false);

        if (activeView is null)
            return false;

        var closeResult = await activeView.CloseAsync(reason: ViewCloseReason.Back, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return closeResult.WasClosed;
    }

    public Task DestroyAsync(CancellationToken cancellationToken = default)
    {
        return RunOnUiThreadAsync(() =>
        {
            if (_isDestroyed)
                return;

            _isDestroyed = true;
            _pageStack.Clear();
            _modalStack.Clear();
            _modalPresenter.Content = null;
            _modalOverlay.IsVisible = false;
            UpdateVisibility();
            Destroyed?.Invoke(this, EventArgs.Empty);
        });
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        return Dispatcher.UIThread.InvokeAsync(action);
    }

    private static Task RunOnUiThreadAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    private void ThrowIfDestroyed()
    {
        if (_isDestroyed)
            throw new ObjectDisposedException(HostId, "The view host has been destroyed.");
    }

    private void UpdateVisibility()
    {
        var hasActiveView = !_isDestroyed && HasActiveView;
        IsVisible = hasActiveView;
        IsHitTestVisible = hasActiveView;
    }
}
