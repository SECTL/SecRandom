using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Windowing;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;

namespace SecRandom.Views;

public partial class MainWindow : FAAppWindow
{
    private readonly bool _usesPrimarySettings;
    private readonly BasicSettingsConfig? _settings;
    private readonly MainConfigHandler? _configHandler;
    private bool _hasBeenShown;
    private bool _windowSizeSavePending;

    public MainWindow() : this(false)
    {
    }

    public MainWindow(bool usesPrimarySettings)
    {
        _usesPrimarySettings = usesPrimarySettings;
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;

        // 覆盖标题栏按钮颜色
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        if (!_usesPrimarySettings)
            return;

        _configHandler = IAppHost.GetService<MainConfigHandler>();
        _settings = _configHandler.Data.General.Basic;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        PropertyChanged += MainWindowOnPropertyChanged;
        Closing += MainWindowOnClosing;
        Closed += MainWindowOnClosed;
        RestorePrimaryWindowSettings();
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

        _hasBeenShown = true;
    }

    private void RestorePrimaryWindowSettings()
    {
        if (_settings is null)
            return;

        Topmost = _settings.MainWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        if (!_settings.AutoSaveWindowSize)
            return;

        Width = Math.Max(MinWidth, _settings.MainWindowWidth);
        Height = Math.Max(MinHeight, _settings.MainWindowHeight);
        if (_settings.MainWindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BasicSettingsConfig.MainWindowTopmostMode))
            Topmost = _settings!.MainWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        else if (e.PropertyName == nameof(BasicSettingsConfig.AutoSaveWindowSize)
                 && _settings!.AutoSaveWindowSize)
            SavePrimaryWindowSize();
    }

    private void MainWindowOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!_usesPrimarySettings || !_hasBeenShown || _settings?.AutoSaveWindowSize != true)
            return;

        if (e.Property == BoundsProperty || e.Property == WindowStateProperty)
            QueueWindowSizeSave();
    }

    private void QueueWindowSizeSave()
    {
        if (_windowSizeSavePending)
            return;

        _windowSizeSavePending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _windowSizeSavePending = false;
            SavePrimaryWindowSize();
        }, TimeSpan.FromMilliseconds(300));
    }

    private void SavePrimaryWindowSize()
    {
        if (_settings?.AutoSaveWindowSize != true || !IsVisible)
            return;

        _settings.MainWindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState != WindowState.Maximized)
        {
            _settings.MainWindowWidth = Math.Max(MinWidth, Bounds.Width);
            _settings.MainWindowHeight = Math.Max(MinHeight, Bounds.Height);
        }

        _configHandler!.Save();
    }

    private void MainWindowOnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_usesPrimarySettings)
            return;

        SavePrimaryWindowSize();
        if (_settings?.BackgroundResident == true && !App.Current.IsStopping)
        {
            e.Cancel = true;
            Hide();
        }
        else if (!App.Current.IsStopping)
        {
            e.Cancel = true;
            App.RequestExitFromMainWindow();
        }
    }

    private void MainWindowOnClosed(object? sender, EventArgs e)
    {
        if (_settings is not null)
            _settings.PropertyChanged -= SettingsOnPropertyChanged;

        PropertyChanged -= MainWindowOnPropertyChanged;
        Closing -= MainWindowOnClosing;
        Closed -= MainWindowOnClosed;
    }
}
