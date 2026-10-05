using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.ViewModels;

namespace SecRandom.Views.SettingsPages.Personalized;

/// <summary>
///     计时器设置：计时进行到设定时长之后自动缩成小窗。
/// </summary>
/// <remarks>
///     页面只写 <c>MainConfigModel.TimerSettings</c>：真正的窗口切换在应用层的 <c>TimerViewService</c>
///     里（它订阅 <c>TimerViewModel.AutoMiniWindowRequested</c>），设置改完不需要重启，下一次计时就生效。
/// </remarks>
[PageInfo("settings.personalized.timer", FluentIcons.TimerFilled, "settings.personalized")]
public partial class TimerSettingsPage : UserControl
{
    private bool _isSettingsSubscribed;

    public TimerSettingsPage()
    {
        Settings = ViewModel.Config.TimerSettings;
        var repaired = Math.Clamp(Settings.AutoMiniWindowAfterSeconds,
            TimerSettingsConfig.MinAutoMiniWindowSeconds, TimerSettingsConfig.MaxAutoMiniWindowSeconds);
        var needsSave = repaired != Settings.AutoMiniWindowAfterSeconds;
        Settings.AutoMiniWindowAfterSeconds = repaired;
        DataContext = this;
        InitializeComponent();
        SubscribeSettings();
        if (needsSave)
            ConfigHandler.Save();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public TimerSettingsConfig Settings { get; }

    // NumericUpDown 的 Minimum/Maximum 是 decimal：边界值从模型常量来，页面上不另写两个数字
    public decimal MinimumAutoMiniWindowSeconds => TimerSettingsConfig.MinAutoMiniWindowSeconds;
    public decimal MaximumAutoMiniWindowSeconds => TimerSettingsConfig.MaxAutoMiniWindowSeconds;

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    private void OnLoaded(object? sender, RoutedEventArgs e) => SubscribeSettings();

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (!_isSettingsSubscribed)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _isSettingsSubscribed = false;
    }

    private void SubscribeSettings()
    {
        if (_isSettingsSubscribed)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _isSettingsSubscribed = true;
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e) => ConfigHandler.Save();
}
