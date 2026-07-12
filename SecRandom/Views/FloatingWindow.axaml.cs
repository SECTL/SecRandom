using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class FloatingWindow : Window
{
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
            ? size * 5
            : double.NaN;
    }

    private static int GetButtonSize(int value)
    {
        return value <= 6
            ? value switch { 0 => 28, 1 => 32, 2 => 40, 3 => 48, 4 => 56, 5 => 64, _ => 72 }
            : System.Math.Clamp(value, 28, 72);
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
            MinWidth = displayStyle switch
            {
                1 => size,
                2 => size * 1.8,
                _ => size * 2.4
            },
            Margin = new Thickness(2),
            Padding = new Thickness(System.Math.Max(4, size * 0.15))
        };

        ToolTip.SetTip(button, label);
        button.Content = displayStyle switch
        {
            1 => new FluentIcon(icon, size * 0.55),
            2 => new TextBlock
            {
                Text = label,
                FontSize = System.Math.Max(12, size * 0.38),
                VerticalAlignment = VerticalAlignment.Center
            },
            _ => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = System.Math.Max(4, size * 0.12),
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new FluentIcon(icon, size * 0.45),
                    new TextBlock
                    {
                        Text = label,
                        FontSize = System.Math.Max(12, size * 0.34),
                        VerticalAlignment = VerticalAlignment.Center
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

        ViewModel.Config.FloatPosition = new FloatPositionConfig { X = Position.X, Y = Position.Y };
    }
}
