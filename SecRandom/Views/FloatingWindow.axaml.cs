using System.Collections.Generic;
using System.ComponentModel;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class FloatingWindow : Window
{
    private bool _isMovingWindow;
    private bool _isPendingWindowDrag;
    private bool _isDocked;
    private bool _isDockedOnLeft;
    private int _dockRevision;
    private int _snapAnimationRevision;
    private bool _isMovingDockHandle;
    private bool _dockHandleWasDragged;
    private PixelPoint _dockDragStartScreenPoint;
    private PixelPoint _dockDragStartPosition;
    private PixelPoint _windowDragStartScreenPoint;
    private PixelPoint _windowDragStartPosition;
    private Screen? _windowDragScreen;
    private PixelRect? _dockWorkingArea;
    private int _dockTransitionRevision;
    private bool _isDockTransitioning;
    private int _expandedWindowWidth;
    private int _expandedWindowHeight;
    private int _dockAnchorCenterY;

    public FloatingWindow()
    {
        DataContext = this;
        Position = new PixelPoint(ViewModel.Config.FloatPosition.X, ViewModel.Config.FloatPosition.Y);
        InitializeComponent();

        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);

        Closing += OnClosing;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ViewModel.Config.FloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        RefreshItems();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public bool CanClose { get; set; } = false;

    public void RefreshItems()
    {
        var settings = ViewModel.Config.FloatingWindowSettings;
        ApplyWindowSettings(settings);
        ButtonsPanel.Children.Clear();
        foreach (var controlName in GetVisibleButtonNames(settings))
        {
            var control = controlName switch
            {
                "roll_call" => GetRollCallButton(settings),
                "quick_draw" => GetQuickDrawButton(settings),
                "lottery" => GetLotteryButton(settings),
                "face_draw" => GetFaceDrawButton(settings),
                _ => null
            };

            if (control != null)
                ButtonsPanel.Children.Add(control);
        }
    }

    private void ApplyWindowSettings(FloatingWindowSettingsConfig settings)
    {
        var size = GetButtonSize(settings.FloatingWindowSize);
        WindowBorder.Opacity = System.Math.Clamp(settings.FloatingWindowOpacity, 20, 100) / 100.0;
        Topmost = settings.FloatingWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        ButtonsPanel.Orientation = settings.FloatingWindowPlacement == 1
            ? Orientation.Vertical
            : Orientation.Horizontal;
        ButtonsPanel.Width = settings.FloatingWindowPlacement == 0
            ? (size + 4) * 2
            : double.NaN;

        if (!settings.StickToEdge && _isDocked)
            RestoreFromDock();
        else if (_isDocked)
        {
            UpdateDockButton();
            Dispatcher.UIThread.Post(() => RepositionDockedWindow(), DispatcherPriority.Render);
        }
    }

    private static int GetButtonSize(int value)
    {
        return value <= 6
            ? value switch { 0 => 28, 1 => 32, 2 => 40, 3 => 48, 4 => 56, 5 => 64, _ => 72 }
            : System.Math.Clamp(value, 32, 160);
    }

    private static IEnumerable<string> GetVisibleButtonNames(FloatingWindowSettingsConfig settings)
    {
        if (settings.ShowRollCallButton) yield return "roll_call";
        if (settings.ShowQuickDrawButton) yield return "quick_draw";
        if (settings.ShowLotteryButton) yield return "lottery";
        if (settings.ShowFaceDrawButton) yield return "face_draw";
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshItems();
    }

    private static Button GetRollCallButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton("\uECAA", Langs.Common.Resources.Feat_RollCall, settings);

        b.Click += (sender, args) =>
        {
            App.ToggleMainWindow("main.rollCall");
        };

        return b;
    }

    private static Button GetQuickDrawButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton("\uE84E", Langs.Common.Resources.Feat_QuickDraw, settings);

        b.Click += (sender, args) =>
        {
            App.ShowQuickDrawWindow();
        };

        return b;
    }

    private static Button GetLotteryButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton("\uE8EC", Langs.Common.Resources.Feat_Lottery, settings);

        b.Click += (sender, args) =>
        {
            App.ToggleMainWindow("main.lottery");
        };

        return b;
    }

    private static Button GetFaceDrawButton(FloatingWindowSettingsConfig settings)
    {
        return CreateButton("\uF3EE", Langs.Common.Resources.Feat_FaceDraw, settings);
    }

    private static Button CreateButton(
        string icon,
        string label,
        FloatingWindowSettingsConfig settings)
    {
        var size = GetButtonSize(settings.FloatingWindowSize);
        var displayStyle = settings.FloatingWindowDisplayStyle;
        var button = new Button
        {
            Height = size,
            Width = size,
            Margin = new Thickness(2),
            Padding = new Thickness(System.Math.Max(2, size * 0.08)),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        ToolTip.SetTip(button, label);
        button.Content = displayStyle switch
        {
            1 => new FluentIcon(icon, size * 0.64),
            2 => new TextBlock
            {
                Text = label,
                FontSize = System.Math.Max(10, size * 0.28),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            },
            _ => new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = System.Math.Max(1, size * 0.04),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FluentIcon(icon, size * 0.46),
                    new TextBlock
                    {
                        Text = label,
                        FontSize = System.Math.Max(9, size * 0.22),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        return button;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!CanClose) e.Cancel = true;
        else ViewModel.Config.FloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Dispatcher.UIThread.Post(RestoreStartupPositionAndScheduleDock, DispatcherPriority.Render);
    }

    private async void RestoreStartupPositionAndScheduleDock()
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        if (IsFullyVisibleOnAnyScreen(Position, width, height))
        {
            ScheduleDockIfAtEdge();
            return;
        }

        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        _isDockedOnLeft = Position.X + width / 2 <= workingArea.Value.Center.X;
        _dockWorkingArea = workingArea.Value;
        _dockAnchorCenterY = Math.Clamp(
            Position.Y + height / 2,
            workingArea.Value.Y + height / 2,
            Math.Max(workingArea.Value.Y + height / 2, workingArea.Value.Bottom - height / 2));
        Opacity = 0;
        _isDocked = true;
        ExpandedContent.IsVisible = false;
        DockButton.Opacity = 1;
        UpdateDockButton();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
        RepositionDockedWindow();
        Opacity = 1;
        SavePosition();
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (ViewModel.Config.FloatingWindowSettings.Draggable
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var source = e.Source as Control;

            if (_isDocked && IsDockButtonChild(source))
            {
                ++_dockRevision;
                _isMovingDockHandle = true;
                _dockHandleWasDragged = false;
                _dockDragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this));
                _dockDragStartPosition = Position;
                e.Pointer.Capture(this);
                return;
            }

            ++_dockRevision;
            ++_snapAnimationRevision;
            _isPendingWindowDrag = true;
            _isMovingWindow = false;
            _windowDragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this));
            _windowDragStartPosition = Position;
            _windowDragScreen = GetScreenForWindow(Position);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isMovingDockHandle)
        {
            MoveDockHandle(e);
            return;
        }

        if (!_isPendingWindowDrag)
            return;

        var pointerPosition = Position + ToPixelPoint(e.GetPosition(this));
        var deltaX = pointerPosition.X - _windowDragStartScreenPoint.X;
        var deltaY = pointerPosition.Y - _windowDragStartScreenPoint.Y;
        if (!_isMovingWindow && Math.Abs(deltaX) < 4 && Math.Abs(deltaY) < 4)
            return;

        _isMovingWindow = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        Position = ConstrainDragPosition(
            new PixelPoint(
                _windowDragStartPosition.X + deltaX,
                _windowDragStartPosition.Y + deltaY),
            pointerPosition);
    }

    private void MoveDockHandle(PointerEventArgs e)
    {
        var workingArea = GetScreenForWindow(Position)?.WorkingArea;
        if (workingArea is null)
            return;

        var pointerPosition = Position + ToPixelPoint(e.GetPosition(this));
        var deltaY = pointerPosition.Y - _dockDragStartScreenPoint.Y;
        _dockHandleWasDragged |= Math.Abs(deltaY) > 2;
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var y = Math.Clamp(
            _dockDragStartPosition.Y + deltaY,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        Position = new PixelPoint(_dockDragStartPosition.X, y);
        _dockAnchorCenterY = y + height / 2;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isMovingDockHandle)
        {
            _isMovingDockHandle = false;
            e.Pointer.Capture(null);
            if (_dockHandleWasDragged)
                SavePosition();
            else
                RestoreFromDock();

            e.Handled = true;
            return;
        }

        if (!_isPendingWindowDrag)
            return;

        _isPendingWindowDrag = false;
        if (!_isMovingWindow)
            return;

        _isMovingWindow = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (ViewModel.Config.FloatingWindowSettings.StickToEdge)
            _ = SnapToNearestEdgeAsync();
        else
            SavePosition();
    }

    private async Task SnapToNearestEdgeAsync()
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
        {
            SavePosition();
            return;
        }

        const int snapDistance = 36;
        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > snapDistance)
        {
            SavePosition();
            return;
        }

        _isDockedOnLeft = distanceToLeft <= distanceToRight;
        _dockWorkingArea = workingArea.Value;
        var targetX = _isDockedOnLeft
            ? workingArea.Value.X
            : workingArea.Value.Right - width;
        var targetY = Math.Clamp(
            Position.Y,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        var start = Position;
        var animationRevision = ++_snapAnimationRevision;

        const int durationMilliseconds = 180;
        const int frameMilliseconds = 15;
        for (var elapsed = 0; elapsed < durationMilliseconds; elapsed += frameMilliseconds)
        {
            await Task.Delay(frameMilliseconds);
            if (animationRevision != _snapAnimationRevision)
                return;

            var progress = Math.Min(1.0, (elapsed + frameMilliseconds) / (double)durationMilliseconds);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Position = new PixelPoint(
                (int)Math.Round(start.X + (targetX - start.X) * eased),
                (int)Math.Round(start.Y + (targetY - start.Y) * eased));
        }

        if (animationRevision != _snapAnimationRevision)
            return;

        Position = new PixelPoint(targetX, targetY);
        SavePosition();
        ScheduleDock();
    }

    private void MoveToDockedEdge(PixelRect workingArea, int width, int height)
    {
        var x = _isDockedOnLeft
            ? workingArea.X
            : workingArea.Right - width;
        var y = Math.Clamp(
            _dockAnchorCenterY - height / 2,
            workingArea.Y,
            Math.Max(workingArea.Y, workingArea.Bottom - height));
        Position = new PixelPoint(x, y);
    }

    private void RepositionDockedWindow(bool restoring = false)
    {
        if (!_isDocked && !restoring)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        MoveToDockedEdge(
            workingArea.Value,
            width,
            height);
    }

    private void ScheduleDock()
    {
        var seconds = Math.Clamp(ViewModel.Config.FloatingWindowSettings.StickToEdgeRecoverSeconds, 0, 60);
        var dockRevision = ++_dockRevision;
        if (seconds == 0)
            return;

        DispatcherTimer.RunOnce(() =>
        {
            if (dockRevision == _dockRevision
                && !_isDocked
                && ViewModel.Config.FloatingWindowSettings.StickToEdge)
                _ = CollapseToDockAsync();
        }, TimeSpan.FromSeconds(seconds));
    }

    private void ScheduleDockIfAtEdge()
    {
        if (_isDocked || !ViewModel.Config.FloatingWindowSettings.StickToEdge)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        const int snapDistance = 36;
        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > snapDistance)
            return;

        _isDockedOnLeft = distanceToLeft <= distanceToRight;
        _dockWorkingArea = workingArea.Value;
        ScheduleDock();
    }

    private async Task CollapseToDockAsync()
    {
        if (_isDocked || _isDockTransitioning || !ViewModel.Config.FloatingWindowSettings.StickToEdge)
            return;

        _isDockTransitioning = true;
        try
        {
            var transitionRevision = ++_dockTransitionRevision;
            CaptureExpandedWindowSize();
            _isDocked = true;
            await AnimateControlAsync(
                ExpandedContent,
                1,
                0,
                1,
                0.9,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            Opacity = 0;
            ExpandedContent.IsVisible = false;
            DockButton.Opacity = 0;
            UpdateDockButton();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            RepositionDockedWindow();
            Opacity = 1;
            await AnimateControlAsync(
                DockButton,
                0,
                1,
                0.85,
                1,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision == _dockTransitionRevision)
                SavePosition();
        }
        finally
        {
            _isDockTransitioning = false;
        }
    }

    private void UpdateDockButton()
    {
        var style = ViewModel.Config.FloatingWindowSettings.StickToEdgeDisplayStyle;
        var size = Math.Clamp(ViewModel.Config.FloatingWindowSettings.DockedWindowSize, 28, 96);
        var glyph = _isDockedOnLeft ? ">" : "<";
        DockButton.Content = style switch
        {
            0 => new FluentIcon("\uECAA", size * 0.62),
            1 => new TextBlock
            {
                Text = "抽",
                FontSize = Math.Max(12, size * 0.42),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            _ => new TextBlock
            {
                Text = glyph,
                FontSize = Math.Max(14, size * 0.52),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        DockButton.Width = size;
        DockButton.Height = size;
        DockButton.Padding = new Thickness(Math.Max(2, size * 0.08));
        DockButton.IsVisible = _isDocked;
    }

    private void DockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RestoreFromDock();
    }

    private static bool IsDockButtonChild(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Button { Name: "DockButton" })
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private PixelPoint ToPixelPoint(Point point)
    {
        return new PixelPoint(
            (int)Math.Round(point.X * RenderScaling),
            (int)Math.Round(point.Y * RenderScaling));
    }

    private async void RestoreFromDock()
    {
        if (!_isDocked || _isDockTransitioning)
            return;

        _isDockTransitioning = true;
        ++_dockRevision;
        try
        {
            var transitionRevision = ++_dockTransitionRevision;
            _isDocked = false;
            await AnimateControlAsync(
                DockButton,
                DockButton.Opacity,
                0,
                1,
                0.85,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            DockButton.IsVisible = false;
            Opacity = 0;
            PositionExpandedWindowAtDockAnchor();
            ExpandedContent.Opacity = 0;
            ExpandedContent.IsVisible = true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            PositionExpandedWindowAtDockAnchor(useCurrentSize: true);
            Opacity = 1;
            await AnimateControlAsync(
                ExpandedContent,
                0,
                1,
                0.9,
                1,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            SavePosition();
            ScheduleDock();
        }
        finally
        {
            _isDockTransitioning = false;
        }
    }

    private void CaptureExpandedWindowSize()
    {
        _expandedWindowWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        _expandedWindowHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        _dockAnchorCenterY = Position.Y + _expandedWindowHeight / 2;
    }

    private RelativePoint GetDockTransformOrigin()
    {
        return new RelativePoint(_isDockedOnLeft ? 0 : 1, 0.5, RelativeUnit.Relative);
    }

    private void PositionExpandedWindowAtDockAnchor(bool useCurrentSize = false)
    {
        var width = useCurrentSize
            ? Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling))
            : _expandedWindowWidth;
        var height = useCurrentSize
            ? Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling))
            : _expandedWindowHeight;
        if (width <= 1 || height <= 1)
            return;

        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        var x = _isDockedOnLeft ? workingArea.Value.X : workingArea.Value.Right - width;
        var y = Math.Clamp(
            _dockAnchorCenterY - height / 2,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        Position = new PixelPoint(x, y);
    }

    private PixelPoint ConstrainDragPosition(PixelPoint requestedPosition, PixelPoint pointerPosition)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var sourceScreen = _windowDragScreen ?? GetScreenForWindow(Position, width, height);
        if (sourceScreen is null)
            return requestedPosition;

        var targetScreen = GetScreenAt(pointerPosition);
        if (targetScreen is not null
            && !ReferenceEquals(sourceScreen, targetScreen)
            && TryConstrainAcrossAdjacentScreens(
                requestedPosition,
                width,
                height,
                sourceScreen.Bounds,
                targetScreen.Bounds,
                sourceScreen.WorkingArea,
                targetScreen.WorkingArea,
                out var constrainedPosition))
        {
            if (IsWithinWorkingArea(constrainedPosition, width, height, targetScreen.WorkingArea))
                _windowDragScreen = targetScreen;

            return constrainedPosition;
        }

        return ClampToWorkingArea(requestedPosition, width, height, sourceScreen.WorkingArea);
    }

    private Screen? GetScreenForWindow(PixelPoint position, int? width = null, int? height = null)
    {
        var center = new PixelPoint(
            position.X + (width ?? Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling))) / 2,
            position.Y + (height ?? Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling))) / 2);
        return GetScreenAt(center)
            ?? GetScreenAt(position)
            ?? Screens.ScreenFromPoint(center)
            ?? Screens.Primary;
    }

    private Screen? GetScreenAt(PixelPoint point)
    {
        foreach (var screen in Screens.All)
        {
            var area = screen.Bounds;
            if (point.X >= area.X
                && point.X < area.Right
                && point.Y >= area.Y
                && point.Y < area.Bottom)
                return screen;
        }

        return null;
    }

    private static bool TryConstrainAcrossAdjacentScreens(
        PixelPoint requestedPosition,
        int width,
        int height,
        PixelRect sourceBounds,
        PixelRect targetBounds,
        PixelRect sourceWorkingArea,
        PixelRect targetWorkingArea,
        out PixelPoint constrainedPosition)
    {
        if (sourceBounds.Right == targetBounds.X || targetBounds.Right == sourceBounds.X)
        {
            var top = Math.Max(sourceWorkingArea.Y, targetWorkingArea.Y);
            var bottom = Math.Min(sourceWorkingArea.Bottom, targetWorkingArea.Bottom);
            if (bottom - top < height)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(
                    requestedPosition.X,
                    Math.Min(sourceWorkingArea.X, targetWorkingArea.X),
                    Math.Max(sourceWorkingArea.Right, targetWorkingArea.Right) - width),
                Math.Clamp(requestedPosition.Y, top, bottom - height));
            return true;
        }

        if (sourceBounds.Bottom == targetBounds.Y || targetBounds.Bottom == sourceBounds.Y)
        {
            var left = Math.Max(sourceWorkingArea.X, targetWorkingArea.X);
            var right = Math.Min(sourceWorkingArea.Right, targetWorkingArea.Right);
            if (right - left < width)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(requestedPosition.X, left, right - width),
                Math.Clamp(
                    requestedPosition.Y,
                    Math.Min(sourceWorkingArea.Y, targetWorkingArea.Y),
                    Math.Max(sourceWorkingArea.Bottom, targetWorkingArea.Bottom) - height));
            return true;
        }

        constrainedPosition = default;
        return false;
    }

    private static bool IsWithinWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return position.X >= workingArea.X
            && position.X + width <= workingArea.Right
            && position.Y >= workingArea.Y
            && position.Y + height <= workingArea.Bottom;
    }

    private bool IsFullyVisibleOnAnyScreen(PixelPoint position, int width, int height)
    {
        foreach (var screen in Screens.All)
        {
            if (IsWithinWorkingArea(position, width, height, screen.WorkingArea))
                return true;
        }

        return false;
    }

    private static PixelPoint ClampToWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, Math.Max(workingArea.X, workingArea.Right - width)),
            Math.Clamp(position.Y, workingArea.Y, Math.Max(workingArea.Y, workingArea.Bottom - height)));
    }

    private static async Task AnimateControlAsync(
        Control control,
        double fromOpacity,
        double toOpacity,
        double fromScale,
        double toScale,
        RelativePoint transformOrigin,
        int transitionRevision)
    {
        control.RenderTransformOrigin = transformOrigin;
        var scaleTransform = new ScaleTransform(fromScale, fromScale);
        control.RenderTransform = scaleTransform;
        control.Opacity = fromOpacity;

        const int durationMilliseconds = 150;
        const int frameMilliseconds = 15;
        for (var elapsed = 0; elapsed < durationMilliseconds; elapsed += frameMilliseconds)
        {
            await Task.Delay(frameMilliseconds);
            var progress = Math.Min(1.0, (elapsed + frameMilliseconds) / (double)durationMilliseconds);
            var eased = 1 - Math.Pow(1 - progress, 3);
            control.Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
            var scale = fromScale + (toScale - fromScale) * eased;
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
        }

        control.Opacity = toOpacity;
        scaleTransform.ScaleX = toScale;
        scaleTransform.ScaleY = toScale;
    }

    private void SavePosition()
    {
        ViewModel.Config.FloatPosition = new FloatPositionConfig { X = Position.X, Y = Position.Y };
    }
}
