using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
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

        // ViewModel.Config.FloatingWindowSettings.PropertyChanged += (sender, args) =>
        // {
        //     OnLoaded(this, new RoutedEventArgs());
        // };
        //
        // ViewModel.Config.FloatingWindowSettings.FloatingWindowButtonControl.CollectionChanged += (sender, args) =>
        // {
        //     CheckIsVisibleValidate();
        //     RefreshItems();
        // };

        CheckIsVisibleValidate();
        RefreshItems();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public bool CanClose { get; set; } = false;

    private void CheckIsVisibleValidate()
    {
        // var settings = ViewModel.Config.FloatingWindowSettings;
        // if (!settings.FloatingWindowButtonControl.Any())
        // {
        //     _ = MakeIsVisibleValidate();
        // }
    }

    private async Task MakeIsVisibleValidate()
    {
        await Task.Delay(1);
        Dispatcher.UIThread.Invoke(() =>
        {
            // ViewModel.Config.FloatingWindowSettings.FloatingWindowButtonControl.Add("roll_call");
        });
    }

    public void RefreshItems()
    {
        RootStackPanel.Children.Clear();
        RootStackPanel.Children.Add(new TouchDragThumb { Orientation = Orientation.Horizontal, Height = 24 });

        foreach (var controlName in
                 (List<string>)["roll_call"]) // ViewModel.Config.FloatingWindowSettings.FloatingWindowButtonControl
        {
            var control = controlName switch
            {
                "roll_call" => GetRollCallButton(),
                "quick_draw" => GetQuickDrawButton(),
                "lottery" => GetLotteryButton(),
                "face_draw" => GetFaceDrawButton(),
                "timer" => GetTimerButton(),
                _ => null
            };

            if (control == null)
            {
                RootStackPanel.Children.Add(new TextBlock { Text = controlName });
                continue;
            }

            RootStackPanel.Children.Add(control);
        }
    }

    private static FACommandBarButton GetRollCallButton()
    {
        var b = new FACommandBarButton
        {
            IconSource = new FluentIconSource("\uECAA"),
            Label = Langs.Common.Resources.Feat_RollCall
        };

        b.Click += (sender, args) =>
        {
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.rollCall");
        };

        return b;
    }

    private static FACommandBarButton GetQuickDrawButton()
    {
        var b = new FACommandBarButton
        {
            IconSource = new FluentIconSource("\uE84E"),
            Label = Langs.Common.Resources.Feat_QuickDraw
        };

        b.Click += (sender, args) =>
        {
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.quickDraw");
        };

        return b;
    }

    private static FACommandBarButton GetLotteryButton()
    {
        var b = new FACommandBarButton
        {
            IconSource = new FluentIconSource("\uE8EC"),
            Label = Langs.Common.Resources.Feat_Lottery
        };

        b.Click += (sender, args) =>
        {
            App.ShowMainWindow();
            MainView.Current?.SelectNavigationItemById("main.lottery");
        };

        return b;
    }

    private static FACommandBarButton GetFaceDrawButton()
    {
        var b = new FACommandBarButton
        {
            IconSource = new FluentIconSource("\uF3EE"),
            Label = Langs.Common.Resources.Feat_FaceDraw
        };

        return b;
    }

    private static FACommandBarButton GetTimerButton()
    {
        var b = new FACommandBarButton
        {
            IconSource = new FluentIconSource("\uF360"),
            Label = Langs.Common.Resources.Feat_Timer
        };

        return b;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!CanClose) e.Cancel = true;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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
            if (visual is Button or FACommandBarButton)
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
