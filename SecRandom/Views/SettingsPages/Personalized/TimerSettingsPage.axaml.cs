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
///     计时器设置：倒计时/秒表/时钟各自是否自动缩成小窗，以及走到多久之后缩。
/// </summary>
/// <remarks>
///     页面只写 <c>MainConfigModel.TimerSettings</c>：真正的窗口切换在应用层的 <c>TimerViewService</c>
///     里（它订阅 <c>TimerViewModel.AutoMiniWindowRequested</c>），设置改完不需要重启，
///     下一次计时（时钟是下一次显示）就生效；正在走的那一次不会中途被缩下去。
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

    /// <summary>常用值快捷按钮：按钮的 <c>Tag</c> 就是秒数，写进设置后照常走 PropertyChanged 落盘。</summary>
    private void PresetSeconds_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !int.TryParse(tag, out var seconds))
            return;

        Settings.AutoMiniWindowAfterSeconds = Math.Clamp(
            seconds, TimerSettingsConfig.MinAutoMiniWindowSeconds, TimerSettingsConfig.MaxAutoMiniWindowSeconds);
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
