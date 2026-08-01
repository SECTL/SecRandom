using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SecRandom.Core.Views;

namespace SecRandom.Views.Mobile;

/// <summary>
/// The single physical mobile host. It presents the root and independent views through one NavigationPage.
/// </summary>
public sealed partial class MobileViewHost : UserControl, IViewHost
{
    private readonly SingleViewHostProvider _singleViewHostProvider;
    private readonly List<ViewBase> _pageStack = [];
    private readonly List<ViewBase> _modalStack = [];
    private readonly Dictionary<Page, ViewBase> _viewsByPage = [];
    private readonly Dictionary<ViewBase, Page> _pagesByView = [];
    private NavigationPage _navigationPage = null!;
    private bool _isDestroyed;
    private bool _isSynchronizingNavigation;

    public MobileViewHost(SingleViewHostProvider singleViewHostProvider)
    {
        _singleViewHostProvider = singleViewHostProvider;

        InitializeComponent();
        _navigationPage = this.FindControl<NavigationPage>("NavigationPage")!;
        _navigationPage.ModalPopped += NavigationPage_OnModalPopped;
        _singleViewHostProvider.Attach(this);
    }

    public string HostId => MobilePageIds.Root;
    public IReadOnlyList<ViewBase> PageStack => _pageStack;
    public ViewBase? ActiveModalView => _modalStack.LastOrDefault();
    public event EventHandler? Destroyed;

    public Task DetachAsync()
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            DetachCore();
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(DetachCore).GetTask();
    }

    public Task ShowPageAsync(ViewBase view, CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(async () =>
        {
            ThrowIfDestroyed();
            var page = RegisterView(view);
            _pageStack.Add(view);
            await _navigationPage.PushAsync(page);
        });

    public Task ShowModalAsync(ViewBase view, CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(async () =>
        {
            ThrowIfDestroyed();
            var page = RegisterView(view);
            _modalStack.Add(view);
            await _navigationPage.PushModalAsync(page);
        });

    public Task ActivateAsync(ViewBase view, CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(async () =>
        {
            ThrowIfDestroyed();
            if (!_pagesByView.TryGetValue(view, out var page))
                throw new InvalidOperationException("The view is not active in this host.");

            if (_modalStack.Contains(view))
            {
                if (!ReferenceEquals(_navigationPage.CurrentPage, page))
                    await _navigationPage.PushModalAsync(page);
                return;
            }

            if (!_pageStack.Contains(view))
                throw new InvalidOperationException("The view is not active in this host.");
        });

    public Task CloseAsync(ViewBase view, CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(async () =>
        {
            if (!_pagesByView.TryGetValue(view, out var page))
                return;

            _isSynchronizingNavigation = true;
            try
            {
                if (_modalStack.Remove(view))
                {
                    if (ReferenceEquals(_navigationPage.CurrentPage, page))
                        await _navigationPage.PopModalAsync();
                }
                else if (_pageStack.Remove(view) && ReferenceEquals(_navigationPage.CurrentPage, page))
                {
                    await _navigationPage.PopAsync();
                }
            }
            finally
            {
                _isSynchronizingNavigation = false;
                RemoveView(page, view);
            }
        });

    public Task DestroyAsync(CancellationToken cancellationToken = default) => RunOnUiThreadAsync(async () =>
    {
        if (_isDestroyed)
            return;

        _isDestroyed = true;
        _pageStack.Clear();
        _modalStack.Clear();
        _pagesByView.Clear();
        _viewsByPage.Clear();
        await _navigationPage.PopAllModalsAsync();
        await _navigationPage.PopToRootAsync();
        await _navigationPage.ReplaceAsync(new ContentPage());
        Destroyed?.Invoke(this, EventArgs.Empty);
    });

    private void DetachCore()
    {
        _singleViewHostProvider.Detach(this);
    }

    private Page RegisterView(ViewBase view)
    {
        NavigationPage.SetHasBackButton(view, true);
        _pagesByView.Add(view, view);
        _viewsByPage.Add(view, view);
        return view;
    }

    private void RemoveView(Page page, ViewBase view)
    {
        _viewsByPage.Remove(page);
        _pagesByView.Remove(view);
    }

    private void NavigationPage_OnPopped(object? sender, NavigationEventArgs e)
    {
        if (_isSynchronizingNavigation || _isDestroyed || e.Page is not ViewBase view)
            return;

        _ = view.CloseAsync(reason: ViewCloseReason.Back);
    }

    private void NavigationPage_OnModalPopped(object? sender, ModalPoppedEventArgs e)
    {
        if (_isSynchronizingNavigation || _isDestroyed || e.Modal is not ViewBase view)
            return;

        _ = view.CloseAsync(reason: ViewCloseReason.Back);
    }

    private void ThrowIfDestroyed()
    {
        if (_isDestroyed)
            throw new ObjectDisposedException(HostId, "The view host has been destroyed.");
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return action();

        return Dispatcher.UIThread.InvokeAsync(action);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
