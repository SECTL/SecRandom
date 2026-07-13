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
    private readonly MainWindowSettingsScope _settingsScope;
    private readonly BasicSettingsConfig? _settings;
    private readonly MainConfigHandler? _configHandler;
    private bool _hasBeenShown;
    private bool _windowSizeSavePending;
    private double _lastNormalWindowWidth;
    private double _lastNormalWindowHeight;
    private WindowState _lastNonMinimizedWindowState = WindowState.Normal;

    public MainWindow() : this(MainWindowSettingsScope.None)
    {
    }

    internal MainWindow(MainWindowSettingsScope settingsScope)
    {
        _settingsScope = settingsScope;
        InitializeComponent();

        TitleBar.Height = 48;
        TitleBar.ExtendsContentIntoTitleBar = true;

        // 覆盖标题栏按钮颜色
        TitleBar.ButtonHoverBackgroundColor = Color.FromArgb(23, 0, 0, 0);
        TitleBar.ButtonPressedBackgroundColor = Color.FromArgb(52, 0, 0, 0);
        TitleBar.ButtonInactiveForegroundColor = Colors.Gray;

        if (!UsesStoredWindowSettings)
            return;

        _configHandler = IAppHost.GetService<MainConfigHandler>();
        _settings = _configHandler.Data.General.Basic;
        _settings.PropertyChanged += SettingsOnPropertyChanged;
        PropertyChanged += MainWindowOnPropertyChanged;
        Closing += MainWindowOnClosing;
        Closed += MainWindowOnClosed;
        RestoreWindowSettings();
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

        if (WindowState != WindowState.Maximized)
        {
            _lastNormalWindowWidth = Math.Max(MinWidth, Bounds.Width);
            _lastNormalWindowHeight = Math.Max(MinHeight, Bounds.Height);
        }
        else
        {
            _lastNonMinimizedWindowState = WindowState.Maximized;
        }

        _hasBeenShown = true;
    }

    private bool UsesStoredWindowSettings => _settingsScope != MainWindowSettingsScope.None;
    private bool UsesPrimaryWindowSettings => _settingsScope == MainWindowSettingsScope.Primary;

    private void RestoreWindowSettings()
    {
        if (_settings is null)
            return;

        if (UsesPrimaryWindowSettings)
            Topmost = _settings.MainWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        if (!_settings.AutoSaveWindowSize)
            return;

        _lastNormalWindowWidth = GetUsableDimension(
            GetStoredWindowWidth(),
            MinWidth,
            UsesPrimaryWindowSettings ? 1200 : 1000);
        _lastNormalWindowHeight = GetUsableDimension(
            GetStoredWindowHeight(),
            MinHeight,
            UsesPrimaryWindowSettings ? 800 : 720);
        Width = _lastNormalWindowWidth;
        Height = _lastNormalWindowHeight;
        if (GetStoredWindowMaximized())
        {
            _lastNonMinimizedWindowState = WindowState.Maximized;
            WindowState = WindowState.Maximized;
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (UsesPrimaryWindowSettings
            && e.PropertyName == nameof(BasicSettingsConfig.MainWindowTopmostMode))
            Topmost = _settings!.MainWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        else if (e.PropertyName == nameof(BasicSettingsConfig.AutoSaveWindowSize)
                 && _settings!.AutoSaveWindowSize)
            SaveWindowSize();
    }

    private void MainWindowOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (!UsesStoredWindowSettings || !_hasBeenShown)
            return;

        if (e.Property == BoundsProperty && WindowState == WindowState.Normal)
        {
            _lastNormalWindowWidth = Math.Max(MinWidth, Bounds.Width);
            _lastNormalWindowHeight = Math.Max(MinHeight, Bounds.Height);
            if (_settings?.AutoSaveWindowSize == true)
                QueueWindowSizeSave();
        }
        else if (e.Property == WindowStateProperty)
        {
            if (WindowState != WindowState.Minimized)
                _lastNonMinimizedWindowState = WindowState;

            if (WindowState != WindowState.Minimized && _settings?.AutoSaveWindowSize == true)
                QueueWindowSizeSave();
        }
    }

    private void QueueWindowSizeSave()
    {
        if (_windowSizeSavePending)
            return;

        _windowSizeSavePending = true;
        DispatcherTimer.RunOnce(() =>
        {
            _windowSizeSavePending = false;
            SaveWindowSize();
        }, TimeSpan.FromMilliseconds(300));
    }

    private void SaveWindowSize()
    {
        if (_settings?.AutoSaveWindowSize != true || !IsVisible)
            return;

        if (_lastNormalWindowWidth <= 0 || _lastNormalWindowHeight <= 0)
        {
            _lastNormalWindowWidth = GetUsableDimension(
                GetStoredWindowWidth(),
                MinWidth,
                UsesPrimaryWindowSettings ? 1200 : 1000);
            _lastNormalWindowHeight = GetUsableDimension(
                GetStoredWindowHeight(),
                MinHeight,
                UsesPrimaryWindowSettings ? 800 : 720);
        }

        var stateToSave = WindowState == WindowState.Minimized
            ? _lastNonMinimizedWindowState
            : WindowState;
        SetStoredWindowMaximized(stateToSave == WindowState.Maximized);
        if (stateToSave == WindowState.Maximized)
        {
            SetStoredWindowWidth(_lastNormalWindowWidth);
            SetStoredWindowHeight(_lastNormalWindowHeight);
        }
        else if (WindowState == WindowState.Normal)
        {
            _lastNormalWindowWidth = Math.Max(MinWidth, Bounds.Width);
            _lastNormalWindowHeight = Math.Max(MinHeight, Bounds.Height);
            SetStoredWindowWidth(_lastNormalWindowWidth);
            SetStoredWindowHeight(_lastNormalWindowHeight);
        }

        _configHandler!.Save();
    }

    internal void RestoreFromMinimized()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = _lastNonMinimizedWindowState;
    }

    private void MainWindowOnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!UsesStoredWindowSettings)
            return;

        SaveWindowSize();
        if (!UsesPrimaryWindowSettings)
            return;

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

        if (UsesStoredWindowSettings)
        {
            PropertyChanged -= MainWindowOnPropertyChanged;
            Closing -= MainWindowOnClosing;
            Closed -= MainWindowOnClosed;
        }
    }

    private double GetStoredWindowWidth()
    {
        return UsesPrimaryWindowSettings ? _settings!.MainWindowWidth : _settings!.SettingsWindowWidth;
    }

    private double GetStoredWindowHeight()
    {
        return UsesPrimaryWindowSettings ? _settings!.MainWindowHeight : _settings!.SettingsWindowHeight;
    }

    private bool GetStoredWindowMaximized()
    {
        return UsesPrimaryWindowSettings ? _settings!.MainWindowMaximized : _settings!.SettingsWindowMaximized;
    }

    private void SetStoredWindowWidth(double value)
    {
        if (UsesPrimaryWindowSettings)
            _settings!.MainWindowWidth = value;
        else
            _settings!.SettingsWindowWidth = value;
    }

    private void SetStoredWindowHeight(double value)
    {
        if (UsesPrimaryWindowSettings)
            _settings!.MainWindowHeight = value;
        else
            _settings!.SettingsWindowHeight = value;
    }

    private void SetStoredWindowMaximized(bool value)
    {
        if (UsesPrimaryWindowSettings)
            _settings!.MainWindowMaximized = value;
        else
            _settings!.SettingsWindowMaximized = value;
    }

    private static double GetUsableDimension(double value, double minimum, double fallback)
    {
        return double.IsFinite(value) && value >= minimum ? value : Math.Max(minimum, fallback);
    }
}

internal enum MainWindowSettingsScope
{
    None,
    Primary,
    Settings
}
