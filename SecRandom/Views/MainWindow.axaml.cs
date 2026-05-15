using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using SecRandom.Core.Controls;

namespace SecRandom.Views;

public partial class MainWindow : FAAppWindow
{
    public MainWindow()
    {
        SplashScreen = new EmptySplashScreen();
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = FATitleBarHitTestType.Complex;

        // 覆盖标题栏按钮颜色
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (App.IsMicaSupported)
        {
            TransparencyLevelHint = [WindowTransparencyLevel.Mica];
            Background = Brushes.Transparent;
        }
    }
}