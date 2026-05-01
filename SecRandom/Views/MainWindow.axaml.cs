using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using FluentAvalonia.UI.Windowing;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Controls;
using SecRandom.Core.Services.Config;

namespace SecRandom.Views;

public partial class MainWindow : AppWindow
{
    public MainWindow()
    {
        SplashScreen = new EmptySplashScreen();
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;
        TitleBar.TitleBarHitTestType = TitleBarHitTestType.Complex;
        
        RefreshButtonColors();
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

    public void RefreshButtonColors()
    {
        var basicSettings = IAppHost.GetService<MainConfigHandler>().Data.BasicSettings;
        TitleBar.ButtonHoverBackgroundColor = basicSettings.ThemeColor;
        TitleBar.ButtonPressedBackgroundColor = basicSettings.ThemeColor;
    }
}