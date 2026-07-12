using System.Collections.Generic;
using System.ComponentModel;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    private bool _isDocked;
    private bool _isDockedOnLeft;
    private int _dockRevision;

    public FloatingWindow()
    {
        DataContext = this;
        Position = new PixelPoint(ViewModel.Config.FloatPosition.X, ViewModel.Config.FloatPosition.Y);
        InitializeComponent();

        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias);
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);

        Closing += OnClosing;
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
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
        DragThumb.IsEnabled = settings.Draggable;
        ButtonsPanel.Orientation = settings.FloatingWindowPlacement == 1
            ? Orientation.Vertical
            : Orientation.Horizontal;
        ButtonsPanel.Width = settings.FloatingWindowPlacement == 0
            ? (size + 4) * 2
            : double.NaN;

        if (!settings.StickToEdge && _isDocked)
            RestoreFromDock();
        else if (_isDocked)
            UpdateDockButton();
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
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.rollCall");
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
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.lottery");
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
            1 => new FluentIcon(icon, size * 0.70),
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
                    new FluentIcon(icon, size * 0.50),
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
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (ViewModel.Config.FloatingWindowSettings.Draggable
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var source = e.Source as Control;

            if (IsChildOfButton(source)) return;

            _isMovingWindow = true;
            BeginMoveDrag(e);
        }
    }

    private static bool IsChildOfButton(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Button)
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (!_isMovingWindow)
            return;

        _isMovingWindow = false;
        if (ViewModel.Config.FloatingWindowSettings.StickToEdge)
            SnapToNearestEdge();

        SavePosition();
    }

    private void SnapToNearestEdge()
    {
        var workingArea = Screens.ScreenFromPoint(Position)?.WorkingArea ?? Screens.Primary?.WorkingArea;
        if (workingArea is null)
            return;

        var scale = RenderScaling;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale));
        _isDockedOnLeft = Position.X + width / 2 < workingArea.Value.Center.X;
        MoveToDockedEdge(workingArea.Value, width, height);
        ScheduleDock();
    }

    private void MoveToDockedEdge(PixelRect workingArea, int width, int height)
    {
        var x = _isDockedOnLeft
            ? workingArea.X
            : workingArea.Right - width;
        var y = Math.Clamp(Position.Y, workingArea.Y, workingArea.Bottom - height);
        Position = new PixelPoint(x, y);
    }

    private void RepositionDockedWindow(bool restoring = false)
    {
        if (!_isDocked && !restoring)
            return;

        var workingArea = Screens.ScreenFromPoint(Position)?.WorkingArea ?? Screens.Primary?.WorkingArea;
        if (workingArea is null)
            return;

        var scale = RenderScaling;
        MoveToDockedEdge(
            workingArea.Value,
            Math.Max(1, (int)Math.Ceiling(Bounds.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(Bounds.Height * scale)));
    }

    private void ScheduleDock()
    {
        var seconds = Math.Clamp(ViewModel.Config.FloatingWindowSettings.StickToEdgeRecoverSeconds, 0, 60);
        var dockRevision = ++_dockRevision;
        if (seconds == 0)
            return;

        DispatcherTimer.RunOnce(() =>
        {
            if (dockRevision == _dockRevision && ViewModel.Config.FloatingWindowSettings.StickToEdge)
                CollapseToDock();
        }, TimeSpan.FromSeconds(seconds));
    }

    private void CollapseToDock()
    {
        _isDocked = true;
        ButtonsPanel.IsVisible = false;
        DragThumb.IsVisible = false;
        UpdateDockButton();
        Dispatcher.UIThread.Post(RepositionDockedWindow, DispatcherPriority.Layout);
    }

    private void UpdateDockButton()
    {
        var style = ViewModel.Config.FloatingWindowSettings.StickToEdgeDisplayStyle;
        var glyph = _isDockedOnLeft ? ">" : "<";
        DockButton.Content = style switch
        {
            0 => new FluentIcon("\uE8A7", 20),
            1 => "SecRandom",
            _ => glyph
        };
        DockButton.Width = style == 1 ? 88 : 36;
        DockButton.Height = 36;
        DockButton.IsVisible = _isDocked;
    }

    private void DockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RestoreFromDock();
    }

    private void RestoreFromDock()
    {
        ++_dockRevision;
        _isDocked = false;
        DockButton.IsVisible = false;
        DragThumb.IsVisible = true;
        ButtonsPanel.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            RepositionDockedWindow(restoring: true);
            SavePosition();
        }, DispatcherPriority.Layout);
    }

    private void SavePosition()
    {
        ViewModel.Config.FloatPosition = new FloatPositionConfig { X = Position.X, Y = Position.Y };
    }
}
