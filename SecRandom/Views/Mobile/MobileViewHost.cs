using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Logging;
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
    private Control _pageContentRoot = null!;
    private TopLevel? _backRequestTopLevel;
    private readonly ILogger<MobileViewHost>? _logger;
    private IInputPane? _inputPane;
    private TextPresenter? _focusedTextPresenter;
    private CancellationTokenSource? _inputPaneAnimationCancellation;
    private TimeSpan _inputPaneAnimationDuration;
    private IEasing? _inputPaneAnimationEasing;
    private bool _isInputPaneOffsetUpdatePending;
    private bool _isDestroyed;
    private bool _isSynchronizingNavigation;
    private bool _isHandlingBackRequest;

    public MobileViewHost(SingleViewHostProvider singleViewHostProvider, ILogger<MobileViewHost>? logger = null)
    {
        _singleViewHostProvider = singleViewHostProvider;
        _logger = logger;

        InitializeComponent();
        _navigationPage = this.FindControl<NavigationPage>("NavigationPage")!;
        _pageContentRoot = this.FindControl<Control>("PageContentRoot")!;
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
        if (_backRequestTopLevel is not null)
            _backRequestTopLevel.BackRequested -= TopLevel_OnBackRequested;
        _backRequestTopLevel = null;
        _singleViewHostProvider.Detach(this);
    }

    public Task<bool> RequestBackAsync(CancellationToken cancellationToken = default) =>
        RunOnUiThreadAsync(async () =>
        {
            if (_isDestroyed || _pageStack.Count <= 1 || _isHandlingBackRequest)
                return false;

            _isHandlingBackRequest = true;
            try
            {
                var view = _pageStack[^1];
                var result = await view.CloseAsync(
                    reason: ViewCloseReason.Back,
                    cancellationToken: cancellationToken);
                return result.WasClosed;
            }
            finally
            {
                _isHandlingBackRequest = false;
            }
        });

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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!OperatingSystem.IsAndroid())
            return;

        _backRequestTopLevel = TopLevel.GetTopLevel(this);
        if (_backRequestTopLevel is not null)
            _backRequestTopLevel.BackRequested += TopLevel_OnBackRequested;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_backRequestTopLevel is not null)
            _backRequestTopLevel.BackRequested -= TopLevel_OnBackRequested;
        _backRequestTopLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private async void TopLevel_OnBackRequested(object? sender, RoutedEventArgs e)
    {
        if (_isDestroyed || _pageStack.Count <= 1 || _isHandlingBackRequest)
            return;

        e.Handled = true;
        await RequestBackAsync();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        AttachInputPane();
        AddHandler(GotFocusEvent, OnDescendantGotFocus, RoutingStrategies.Bubble, true);
        SizeChanged += OnHostSizeChanged;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        RemoveHandler(GotFocusEvent, OnDescendantGotFocus);
        SizeChanged -= OnHostSizeChanged;
        DetachInputPane();
        ResetPageContentOffset();
        base.OnUnloaded(e);
    }

    private void AttachInputPane()
    {
        DetachInputPane();
        _inputPane = TopLevel.GetTopLevel(this)?.InputPane;
        if (_inputPane is null)
        {
            _logger?.LogWarning("Mobile input pane is unavailable after the view host loaded.");
            return;
        }

        _inputPane.StateChanged += InputPane_OnStateChanged;
        UpdateFocusedTextPresenter();
    }

    private void DetachInputPane()
    {
        if (_inputPane is not null)
            _inputPane.StateChanged -= InputPane_OnStateChanged;
        _inputPane = null;
        DetachFocusedTextPresenter();
        CancelInputPaneAnimation();
    }

    private void InputPane_OnStateChanged(object? sender, InputPaneStateEventArgs e)
    {
        if (!ReferenceEquals(sender, _inputPane))
            return;

        _logger?.LogDebug("Mobile input pane changed to {State}; occluded rectangle: {OccludedRect}",
            e.NewState, e.EndRect);
        UpdateFocusedTextPresenter();
        _isInputPaneOffsetUpdatePending = false;
        if (e.NewState == InputPaneState.Open)
        {
            _inputPaneAnimationDuration = e.AnimationDuration;
            _inputPaneAnimationEasing = e.Easing;
        }

        _ = AnimatePageContentOffsetAsync(
            e.NewState == InputPaneState.Open ? CalculatePageContentOffset(e.EndRect) : 0,
            e.AnimationDuration,
            e.Easing ?? new LinearEasing());
    }

    private void OnDescendantGotFocus(object? sender, FocusChangedEventArgs e)
    {
        UpdateFocusedTextPresenter();
        UpdatePageContentOffsetForOpenInputPane();
    }

    private void OnHostSizeChanged(object? sender, SizeChangedEventArgs e) =>
        UpdatePageContentOffsetForOpenInputPane();

    private void UpdateFocusedTextPresenter()
    {
        var focusedElement = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        var textBox = focusedElement as TextBox ?? focusedElement?.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
        var textPresenter = textBox?.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        if (ReferenceEquals(_focusedTextPresenter, textPresenter))
            return;

        DetachFocusedTextPresenter();
        _focusedTextPresenter = textPresenter;
        if (_focusedTextPresenter is not null)
            _focusedTextPresenter.CaretBoundsChanged += FocusedTextPresenter_OnCaretBoundsChanged;
    }

    private void DetachFocusedTextPresenter()
    {
        if (_focusedTextPresenter is not null)
            _focusedTextPresenter.CaretBoundsChanged -= FocusedTextPresenter_OnCaretBoundsChanged;
        _focusedTextPresenter = null;
    }

    private void FocusedTextPresenter_OnCaretBoundsChanged(object? sender, EventArgs e) =>
        UpdatePageContentOffsetForOpenInputPane();

    private void UpdatePageContentOffsetForOpenInputPane()
    {
        if (_inputPane is not { State: InputPaneState.Open } || _inputPaneAnimationEasing is null)
            return;
        if (_inputPaneAnimationCancellation is not null)
        {
            _isInputPaneOffsetUpdatePending = true;
            return;
        }

        _ = AnimatePageContentOffsetAsync(
            CalculatePageContentOffset(_inputPane.OccludedRect),
            _inputPaneAnimationDuration,
            _inputPaneAnimationEasing);
    }

    private double CalculatePageContentOffset(Rect occludedRect)
    {
        if (occludedRect.Width <= 0 || occludedRect.Height <= 0)
            return 0;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.FocusManager?.GetFocusedElement() is not Visual focusedElement ||
            !_pageContentRoot.IsVisualAncestorOf(focusedElement))
            return 0;

        var avoidanceElement = focusedElement as TextBox ??
                               focusedElement.GetVisualAncestors().OfType<TextBox>().FirstOrDefault() ?? focusedElement;
        var focusedBottom = avoidanceElement.TranslatePoint(
            new Point(0, avoidanceElement.Bounds.Height), topLevel);
        if (focusedBottom is null)
            return 0;

        return Math.Min(0, occludedRect.Top - 12 - focusedBottom.Value.Y);
    }

    private async Task AnimatePageContentOffsetAsync(double targetOffset, TimeSpan duration, IEasing easing)
    {
        if (_pageContentRoot.RenderTransform is not TranslateTransform transform)
            return;

        var startOffset = transform.Y;
        CancelInputPaneAnimation();
        if (duration <= TimeSpan.Zero || Math.Abs(startOffset - targetOffset) < 0.01)
        {
            transform.Y = targetOffset;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _inputPaneAnimationCancellation = cancellation;
        var animation = new Animation
        {
            Duration = duration,
            Easing = new InputPaneAnimationEasing(easing),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(TranslateTransform.YProperty, startOffset) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(TranslateTransform.YProperty, targetOffset) } }
            }
        };

        try
        {
            await animation.RunAsync(transform, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            var updatePendingOffset = ReferenceEquals(_inputPaneAnimationCancellation, cancellation) &&
                                      _isInputPaneOffsetUpdatePending;
            if (ReferenceEquals(_inputPaneAnimationCancellation, cancellation))
            {
                _inputPaneAnimationCancellation = null;
                _isInputPaneOffsetUpdatePending = false;
                if (!cancellation.IsCancellationRequested)
                    transform.Y = targetOffset;
            }
            cancellation.Dispose();
            if (updatePendingOffset)
                UpdatePageContentOffsetForOpenInputPane();
        }
    }

    private void CancelInputPaneAnimation()
    {
        _inputPaneAnimationCancellation?.Cancel();
        _inputPaneAnimationCancellation = null;
    }

    private void ResetPageContentOffset()
    {
        _isInputPaneOffsetUpdatePending = false;
        CancelInputPaneAnimation();
        if (_pageContentRoot.RenderTransform is TranslateTransform transform)
            transform.Y = 0;
    }

    private sealed class InputPaneAnimationEasing(IEasing easing) : Easing
    {
        public override double Ease(double progress) => easing.Ease(progress);
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

    private static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            return action();

        return Dispatcher.UIThread.InvokeAsync(action);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
