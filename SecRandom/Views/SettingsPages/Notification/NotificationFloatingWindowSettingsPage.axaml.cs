using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Helpers;
using SecRandom.Services.Notification;
using LR = SecRandom.Langs.SettingsPages.Notification.Resources;

namespace SecRandom.Views.SettingsPages.Notification;

/// <summary>
///     内置通知浮窗（闪抽结果浮窗）的窗口设置。位置、显示器、偏移与不透明度只有一份，写在
///     <see cref="NotificationSettingsConfig.Default" /> 上；各通知渠道只保留启用、动画、通知服务与交付时长，
///     渠道的窗口覆盖字段已无界面入口，因此新页面的每次编辑就是所有渠道看到的浮窗几何。
/// </summary>
[PageInfo("settings.notification.floatingWindow", FluentIcons.PictureInPictureFilled, "settings.notification")]
public partial class NotificationFloatingWindowSettingsPage : UserControl, INotifyPropertyChanged
{
    private const string MonitorLabelFormatKey = "O_Monitor_Label";
    private const string MonitorPrimaryKey = "O_Monitor_Primary";

    private bool _savePending;
    private DispatcherTimer? _saveTimer;
    private event PropertyChangedEventHandler? NotifyPropertyChanged;

    public NotificationFloatingWindowSettingsPage()
    {
        InitializeComponent();
        Settings = ConfigHandler.Data.NotificationSettings.Default;
        CountdownStyleOptions =
        [
            new CountdownStyleOption((int)NotificationCountdownStyle.Text, Text("Text hint", "O_CountdownStyle_Text")),
            new CountdownStyleOption((int)NotificationCountdownStyle.Edge, Text("Window outline", "O_CountdownStyle_Edge"))
        ];
        CountdownColorModeOptions =
        [
            new CountdownColorModeOption((int)NotificationCountdownColorMode.Gradient,
                Text("Gradient", "O_CountdownColorMode_Gradient")),
            new CountdownColorModeOption((int)NotificationCountdownColorMode.Solid,
                Text("Single color", "O_CountdownColorMode_Solid"))
        ];
        MonitorOptions = [new MonitorOption("", Text("Not specified", "O_Monitor_Unspecified"))];
        if (!string.IsNullOrWhiteSpace(Settings.EnabledMonitor))
            MonitorOptions.Add(new MonitorOption(Settings.EnabledMonitor, Settings.EnabledMonitor));

        // 顺序必须与 NotificationWindowAnchorLayout.GridOrder 一致：左上 → 上 → 右上 → 左 → 中 → 右 → 左下 → 下 → 右下。
        WindowPositionLabels =
        [
            Text("Top left", "O_WindowPosition_TopLeft"),
            Text("Top", "O_WindowPosition_Top"),
            Text("Top right", "O_WindowPosition_TopRight"),
            Text("Left", "O_WindowPosition_Left"),
            Text("Center", "O_WindowPosition_Center"),
            Text("Right", "O_WindowPosition_Right"),
            Text("Bottom left", "O_WindowPosition_BottomLeft"),
            Text("Bottom", "O_WindowPosition_Bottom"),
            Text("Bottom right", "O_WindowPosition_BottomRight")
        ];

        DataContext = this;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private MainConfigHandler ConfigHandler { get; } = IAppHost.GetService<MainConfigHandler>();

    public NotificationChannelSettings Settings { get; }

    public ObservableCollection<MonitorOption> MonitorOptions { get; }

    /// <summary>九宫格各格的文本，顺序与 <see cref="NotificationWindowAnchorLayout.GridOrder" /> 一致。</summary>
    public IReadOnlyList<string> WindowPositionLabels { get; }

    public MonitorOption? SelectedMonitor
    {
        get => MonitorOptions.FirstOrDefault(option => option.Value == Settings.EnabledMonitor);
        set
        {
            if (value is null || value.Value == Settings.EnabledMonitor)
                return;

            Settings.EnabledMonitor = value.Value;
        }
    }

    /// <summary>低配模式会强制关闭自适应前景色，开关因此置灰。</summary>
    public bool IsAdaptiveForegroundAvailable => !UiRenderQuality.IsLowSpecMode;

    /// <summary>
    ///     开关显示的生效值：低配模式下一律显示为关闭，但不去改用户已保存的偏好，
    ///     退出低配模式后开关重新启用并恢复用户自己的选择。
    /// </summary>
    public bool AdaptiveForegroundEnabled
    {
        get => IsAdaptiveForegroundAvailable && Settings.AdaptiveForeground;
        set
        {
            if (!IsAdaptiveForegroundAvailable || Settings.AdaptiveForeground == value)
                return;

            Settings.AdaptiveForeground = value;
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => NotifyPropertyChanged += value;
        remove => NotifyPropertyChanged -= value;
    }

    /// <summary>倒计时样式下拉框的候选：文字提示 / 四周描边。</summary>
    public IReadOnlyList<CountdownStyleOption> CountdownStyleOptions { get; }

    public CountdownStyleOption? SelectedCountdownStyleOption
    {
        get => CountdownStyleOptions.FirstOrDefault(option => option.Value == Settings.CountdownStyle);
        set
        {
            if (value is null || value.Value == Settings.CountdownStyle)
                return;

            Settings.CountdownStyle = value.Value;
        }
    }

    /// <summary>颜色模式下拉框的候选：渐变 / 统一纯色。</summary>
    public IReadOnlyList<CountdownColorModeOption> CountdownColorModeOptions { get; }

    public CountdownColorModeOption? SelectedCountdownColorModeOption
    {
        get => CountdownColorModeOptions.FirstOrDefault(option => option.Value == Settings.CountdownColorMode);
        set
        {
            if (value is null || value.Value == Settings.CountdownColorMode)
                return;

            Settings.CountdownColorMode = value.Value;
        }
    }

    /// <summary>颜色卡只在选中「四周描边」时出现：文字倒计时没有可设置的颜色。</summary>
    public bool ShowCountdownColors => Settings.UsesEdgeCountdown;

    public bool IsCountdownGradientMode =>
        Settings.UsesEdgeCountdown
        && Settings.CountdownColorMode == (int)NotificationCountdownColorMode.Gradient;

    public bool IsCountdownSolidMode =>
        Settings.UsesEdgeCountdown
        && Settings.CountdownColorMode == (int)NotificationCountdownColorMode.Solid;

    /// <summary>
    ///     设置卡里的预览条，与浮窗四周使用同一组颜色：渐变时起始色在左、终止色在右，纯色时满条同色。
    /// </summary>
    public IBrush CountdownPreviewBrush
    {
        get
        {
            var start = Settings.ResolvedCountdownStart;
            var end = Settings.ResolvedCountdownEnd;
            if (Settings.CountdownColorMode != (int)NotificationCountdownColorMode.Gradient)
                return new SolidColorBrush(start);

            return new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(start, 0),
                    new GradientStop(end, 1)
                }
            };
        }
    }

    private void Notify(string propertyName)
    {
        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnPageLoaded(object? sender, RoutedEventArgs e)
    {
        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        Settings.PropertyChanged += SettingsOnPropertyChanged;

        RefreshMonitorOptions();

        // 低配模式可能在基本设置页刚被改过；回到本页时按当前值重算开关的可用性与显示态。
        Notify(nameof(IsAdaptiveForegroundAvailable));
        Notify(nameof(AdaptiveForegroundEnabled));

        // 页面可能被缓存后重新显示，倒计时相关的派生值同样按当前配置重算。
        Notify(nameof(SelectedCountdownStyleOption));
        Notify(nameof(SelectedCountdownColorModeOption));
        Notify(nameof(ShowCountdownColors));
        Notify(nameof(IsCountdownGradientMode));
        Notify(nameof(IsCountdownSolidMode));
        Notify(nameof(CountdownPreviewBrush));
    }

    private void OnPageUnloaded(object? sender, RoutedEventArgs e)
    {
        // 切页或关窗时把挂起的编辑立刻落盘，否则防抖窗口里的最后一次拖动会丢。
        CommitSettings();

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _saveTimer?.Stop();
        _saveTimer = null;
    }

    /// <summary>显示器名称随显示器插拔变化，每次加载与打开下拉框时都重建一次候选列表。</summary>
    private void RefreshMonitorOptions()
    {
        var selectedMonitor = Settings.EnabledMonitor;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Screens is not null)
        {
            var nativeNames = WindowsMonitorNameProvider.GetNames();
            for (var index = 0; index < topLevel.Screens.All.Count; index++)
            {
                var screen = topLevel.Screens.All[index];
                nativeNames.TryGetValue(screen.Bounds.Position, out var nativeName);
                var displayName = string.IsNullOrWhiteSpace(screen.DisplayName) ? nativeName : screen.DisplayName;
                var identifier = NotificationMonitorIdentifier.Get(displayName, screen.Bounds);
                if (MonitorOptions.All(option => option.Value != identifier))
                {
                    var label = NotificationMonitorIdentifier.GetLabel(
                        displayName,
                        screen.Bounds,
                        screen.IsPrimary,
                        index,
                        Text("{0}: {1}x{2} @ {3},{4}{5}", MonitorLabelFormatKey),
                        Text("（主显示器）", MonitorPrimaryKey));
                    MonitorOptions.Add(new MonitorOption(identifier, label));
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(selectedMonitor)
            && MonitorOptions.All(option => option.Value != selectedMonitor))
            MonitorOptions.Add(new MonitorOption(selectedMonitor, selectedMonitor));

        NotifyPropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedMonitor)));
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationChannelSettings.EnabledMonitor))
            Notify(nameof(SelectedMonitor));

        if (e.PropertyName == nameof(NotificationChannelSettings.CountdownStyle))
        {
            Notify(nameof(ShowCountdownColors));
            Notify(nameof(IsCountdownGradientMode));
            Notify(nameof(IsCountdownSolidMode));
            Notify(nameof(SelectedCountdownStyleOption));
        }
        else if (e.PropertyName == nameof(NotificationChannelSettings.CountdownColorMode))
        {
            Notify(nameof(IsCountdownGradientMode));
            Notify(nameof(IsCountdownSolidMode));
            Notify(nameof(SelectedCountdownColorModeOption));
            Notify(nameof(CountdownPreviewBrush));
        }
        else if (e.PropertyName is nameof(NotificationChannelSettings.CountdownGradientStart)
                 or nameof(NotificationChannelSettings.CountdownGradientEnd)
                 or nameof(NotificationChannelSettings.CountdownSolidColor))
        {
            Notify(nameof(CountdownPreviewBrush));
        }

        // 子配置（NotificationSettings.Default）不会自动落盘，页面的每次编辑都要显式保存一次。
        ScheduleSave();
    }

    /// <summary>排一次 400ms 后的落盘，连续编辑（滑杆拖动）会被下一次调用重置。</summary>
    private void ScheduleSave()
    {
        _savePending = true;

        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => CommitSettings();
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void CommitSettings()
    {
        _saveTimer?.Stop();
        if (!_savePending)
            return;

        _savePending = false;
        ConfigHandler.Save();
    }

    private static string Text(string fallback, string key)
    {
        return LR.ResourceManager.GetString(key, LR.Culture) ?? fallback;
    }
}

/// <summary>显示器下拉框的一项。<see cref="Value" /> 是持久化的显示器标识，空串表示跟随主显示器。</summary>
public sealed record MonitorOption(string Value, string Label);

/// <summary>倒计时样式下拉框的一项。<see cref="Value" /> 是持久化的 <see cref="NotificationCountdownStyle" />。</summary>
public sealed record CountdownStyleOption(int Value, string Label);

/// <summary>倒计时配色模式下拉框的一项。</summary>
public sealed record CountdownColorModeOption(int Value, string Label);