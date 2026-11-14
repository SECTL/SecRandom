using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Core.Models.SubConfigs;

public partial class NotificationSettingsConfig : ObservableObject, IJsonOnDeserialized
{
    private bool _hasDefault;
    private bool _hasOverrideDefaultsInitialized;
    private bool _overrideDefaultsInitialized = true;
    private bool _hasBasicSettingsSeparatedInitialized;
    private bool _basicSettingsSeparatedInitialized = true;
    private bool _hasFloatingWindowSettingsUnifiedInitialized;
    private bool _floatingWindowSettingsUnifiedInitialized = true;
    private NotificationChannelSettings _default = NotificationChannelSettings.CreateDisabled();
    private OverridableNotificationChannelSettings _rollCall = new();
    private OverridableNotificationChannelSettings _quickDraw = CreateQuickDraw();
    private OverridableNotificationChannelSettings _lottery = new();
    private FullscreenNotificationSettings _fullscreen = new();

    [AllowNull]
    public NotificationChannelSettings Default
    {
        get => _default;
        set
        {
            _hasDefault = value is not null;
            SetProperty(ref _default, value ?? NotificationChannelSettings.CreateDisabled());
        }
    }

    [AllowNull]
    public OverridableNotificationChannelSettings RollCall
    {
        get => _rollCall;
        set => SetProperty(ref _rollCall, value ?? new OverridableNotificationChannelSettings());
    }

    [AllowNull]
    public OverridableNotificationChannelSettings QuickDraw
    {
        get => _quickDraw;
        set => SetProperty(ref _quickDraw, value ?? CreateQuickDraw());
    }

    [AllowNull]
    public OverridableNotificationChannelSettings Lottery
    {
        get => _lottery;
        set => SetProperty(ref _lottery, value ?? new OverridableNotificationChannelSettings());
    }

    /// <summary>
    ///     全屏结果通知渠道的外观、文案、动画与倒计时设置。
    /// </summary>
    /// <remarks>
    ///     全屏渠道是**独立的第四种渠道值**（<c>NotificationServiceType == 3</c>），
    ///     与内置浮窗、ClassIsland 平级且互斥；这里的设置只服务全屏窗口自己，
    ///     不会改变 QuickDraw 浮窗的任何行为。
    /// </remarks>
    [AllowNull]
    public FullscreenNotificationSettings Fullscreen
    {
        get => _fullscreen;
        set => SetProperty(ref _fullscreen, value ?? new FullscreenNotificationSettings());
    }

    [JsonInclude]
    public bool OverrideDefaultsInitialized
    {
        get => _overrideDefaultsInitialized;
        private set
        {
            _hasOverrideDefaultsInitialized = true;
            _overrideDefaultsInitialized = value;
        }
    }

    [JsonInclude]
    public bool BasicSettingsSeparatedInitialized
    {
        get => _basicSettingsSeparatedInitialized;
        private set
        {
            _hasBasicSettingsSeparatedInitialized = true;
            _basicSettingsSeparatedInitialized = value;
        }
    }

    /// <summary>
    ///     内置通知浮窗的窗口设置（位置 / 显示器 / 偏移 / 不透明度）已从各渠道设置页收归独立的
    ///     「内置通知浮窗」页面，并且只写 <see cref="Default" />，所以渠道的窗口覆盖不再有入口。
    /// </summary>
    /// <remarks>
    ///     旧配置里手工打开的分渠道窗口覆盖在这里折回默认设置，否则会出现一个「看不见也改不掉」的覆盖：
    ///     新页面改的是 Default，而该渠道仍然读自己的旧窗口值。标记本身只在迁移时写一次。
    /// </remarks>
    [JsonInclude]
    public bool FloatingWindowSettingsUnifiedInitialized
    {
        get => _floatingWindowSettingsUnifiedInitialized;
        private set
        {
            _hasFloatingWindowSettingsUnifiedInitialized = true;
            _floatingWindowSettingsUnifiedInitialized = value;
        }
    }

    private static OverridableNotificationChannelSettings CreateQuickDraw()
    {
        var settings = OverridableNotificationChannelSettings.CreateEnabled();
        return settings;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_hasOverrideDefaultsInitialized || !_overrideDefaultsInitialized)
        {
            if (_hasDefault)
            {
                ConfigureLegacyGlobalChannel(RollCall);
                ConfigureLegacyGlobalChannel(QuickDraw);
                ConfigureLegacyGlobalChannel(Lottery);
            }
            else
            {
                RollCall.EnableAllOverrides();
                QuickDraw.EnableAllOverrides();
                Lottery.EnableAllOverrides();
            }
            _overrideDefaultsInitialized = true;
        }

        if (!_hasFloatingWindowSettingsUnifiedInitialized || !_floatingWindowSettingsUnifiedInitialized)
        {
            RollCall.OverrideNotificationWindowSettings = false;
            QuickDraw.OverrideNotificationWindowSettings = false;
            Lottery.OverrideNotificationWindowSettings = false;
            _floatingWindowSettingsUnifiedInitialized = true;
        }

        if (_hasBasicSettingsSeparatedInitialized && _basicSettingsSeparatedInitialized)
            return;

        CopyInheritedBasicSettings(RollCall);
        CopyInheritedBasicSettings(QuickDraw);
        CopyInheritedBasicSettings(Lottery);
        _basicSettingsSeparatedInitialized = true;
    }

    private void CopyInheritedBasicSettings(OverridableNotificationChannelSettings channel)
    {
        if (channel.OverrideBasicSettings)
            return;

        channel.Enabled = Default.Enabled;
        channel.Animation = Default.Animation;
        channel.OverrideBasicSettings = true;
    }

    private static void ConfigureLegacyGlobalChannel(OverridableNotificationChannelSettings channel)
    {
        channel.OverrideBasicSettings = channel.HasExplicitEnabled;
        channel.OverrideNotificationWindowSettings = false;
        channel.OverrideServiceSettings = false;
    }
}

public partial class NotificationChannelSettings : ObservableObject, IJsonOnDeserialized
{
    private bool _enabled;
    private bool _hasExplicitEnabled;
    [ObservableProperty] private bool _animation = true;
    private string _enabledMonitor = "";
    [ObservableProperty] private int _windowPosition = 0;
    [ObservableProperty] private int _horizontalOffset = 0;
    [ObservableProperty] private int _verticalOffset = 0;
    [ObservableProperty] private int _transparency = 80;

    /// <summary>
    ///     内置通知浮窗是否按窗口背后的桌面亮度自适应前景色（默认开启）。
    /// </summary>
    /// <remarks>
    ///     只在采样成功时生效：抓不到桌面（非 Windows、锁屏、远程桌面）就回退应用主题。
    ///     低配模式会强制关闭这一项（DWM 合成与逐像素采样都不该在低配机上跑），
    ///     设置页因此把开关置灰并显示为关闭。
    /// </remarks>
    [ObservableProperty] private bool _adaptiveForeground = true;
    private int _notificationServiceType;
    [ObservableProperty] private int _displayDuration = 3;
    private bool _hasDisplayDuration;
    private int? _legacyAutoCloseTime;
    private bool _useBuiltInOnServiceFailure = true;
    private bool _hasUseBuiltInOnServiceFailure;
    private bool? _legacyUseBuiltInOnClassIslandFailure;
    [ObservableProperty] private bool _useMainWindowWhenExceedThreshold = true;
    [ObservableProperty] private int _mainWindowDisplayThreshold = 5;

    private int _countdownStyle = (int)NotificationCountdownStyle.Edge;
    private int _countdownColorMode = (int)NotificationCountdownColorMode.Gradient;
    private Color _countdownSolidColor = NotificationCountdownPalette.DefaultStart;
    private List<Color> _countdownGradientColors = NotificationCountdownPalette.CreateDefaultColors();

    public bool UseBuiltInOnServiceFailure
    {
        get => _useBuiltInOnServiceFailure;
        set
        {
            _hasUseBuiltInOnServiceFailure = true;
            SetProperty(ref _useBuiltInOnServiceFailure, value);
        }
    }

    [JsonPropertyName("auto_close_time")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? LegacyAutoCloseTime
    {
        get => null;
        set => _legacyAutoCloseTime = value;
    }

    [JsonPropertyName("use_built_in_on_class_island_failure")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyUseBuiltInOnClassIslandFailure
    {
        get => null;
        set => _legacyUseBuiltInOnClassIslandFailure = value;
    }

    [AllowNull]
    public string EnabledMonitor
    {
        get => _enabledMonitor;
        set => SetProperty(
            ref _enabledMonitor,
            string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "OFF", StringComparison.OrdinalIgnoreCase)
                ? ""
                : value);
    }

    public int NotificationServiceType
    {
        get => _notificationServiceType;
        set
        {
            // 0=内置浮窗、1=ClassIsland、2=同时使用（内置+ClassIsland）、3=全屏。
            // 值 3 是追加值，0/1/2 的含义逐字节不变；越界值仍回落 0。
            if (!SetProperty(ref _notificationServiceType, value is >= 0 and <= 3 ? value : 0))
                return;

            OnPropertyChanged(nameof(UsesBuiltInNotificationService));
            OnPropertyChanged(nameof(UsesExternalNotificationService));
            OnPropertyChanged(nameof(UsesFullscreenNotificationService));
        }
    }

    [JsonIgnore]
    public bool UsesBuiltInNotificationService => NotificationServiceType is 0 or 2;

    [JsonIgnore]
    public bool UsesExternalNotificationService => NotificationServiceType is 1 or 2;

    /// <summary>
    ///     是否使用全屏结果通知渠道。与另外两条渠道属性互斥："同时使用"永远只含内置 + ClassIsland。
    /// </summary>
    [JsonIgnore]
    public bool UsesFullscreenNotificationService => NotificationServiceType == 3;

    /// <summary>
    ///     内置通知浮窗的倒计时样式：0 = 文字提示，1 = 四周描边。
    /// </summary>
    /// <remarks>
    ///     和浮窗几何一样是全局外观，只读 <see cref="NotificationSettingsConfig.Default" />，
    ///     但字段仍留在共享的渠道设置类型上（既有 <see cref="AdaptiveForeground" /> 等也是这个形态）。
    ///     默认值由字段初始化器给出，所以旧配置缺这个键时直接是新样式，不需要迁移代码。
    /// </remarks>
    public int CountdownStyle
    {
        get => _countdownStyle;
        set => SetProperty(
            ref _countdownStyle,
            value is (int)NotificationCountdownStyle.Text or (int)NotificationCountdownStyle.Edge
                ? value
                : (int)NotificationCountdownStyle.Edge);
    }

    /// <summary>倒计时颜色模式：0 = 渐变，1 = 纯色。</summary>
    public int CountdownColorMode
    {
        get => _countdownColorMode;
        set => SetProperty(
            ref _countdownColorMode,
            value is (int)NotificationCountdownColorMode.Gradient or (int)NotificationCountdownColorMode.Solid
                ? value
                : (int)NotificationCountdownColorMode.Gradient);
    }

    /// <summary>纯色模式的描边颜色；透明度归一为不透明，浮窗整体透明度另有设置。</summary>
    public Color CountdownSolidColor
    {
        get => _countdownSolidColor;
        set => SetProperty(ref _countdownSolidColor, NotificationCountdownPalette.ToOpaque(value));
    }

    /// <summary>渐变模式的色标，顺序即「右上角 → 左下角」的颜色顺序。</summary>
    public List<Color> CountdownGradientColors
    {
        get => _countdownGradientColors;
        set => SetProperty(ref _countdownGradientColors, NotificationCountdownPalette.NormalizeColors(value));
    }

    [JsonIgnore]
    public bool UsesEdgeCountdown => CountdownStyle == (int)NotificationCountdownStyle.Edge;

    [JsonIgnore]
    public IReadOnlyList<Color> ResolvedCountdownColors =>
        CountdownColorMode == (int)NotificationCountdownColorMode.Solid
            ? [CountdownSolidColor]
            : CountdownGradientColors;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            _hasExplicitEnabled = true;
            SetProperty(ref _enabled, value);
        }
    }

    internal bool HasExplicitEnabled => _hasExplicitEnabled;

    public NotificationChannelSettings()
    {
    }

    private NotificationChannelSettings(bool enabledByDefault)
    {
        _enabled = enabledByDefault;
    }

    public static NotificationChannelSettings CreateDisabled() => new(false);

    protected void SetDefaultEnabled(bool value)
    {
        _enabled = value;
    }

    void IJsonOnDeserialized.OnDeserialized()
    {
        if (!_hasDisplayDuration && _legacyAutoCloseTime is { } legacyAutoCloseTime)
            DisplayDuration = Math.Clamp(legacyAutoCloseTime, 1, 60);

        if (!_hasUseBuiltInOnServiceFailure && _legacyUseBuiltInOnClassIslandFailure is { } legacyValue)
            _useBuiltInOnServiceFailure = legacyValue;

        // 手改/旧版配置文件里的越界值在这里兜底；字段初始化器已保证缺键时采用新倒计时。
        _countdownStyle = _countdownStyle
            is (int)NotificationCountdownStyle.Text or (int)NotificationCountdownStyle.Edge
            ? _countdownStyle
            : (int)NotificationCountdownStyle.Edge;
        _countdownColorMode = _countdownColorMode
            is (int)NotificationCountdownColorMode.Gradient or (int)NotificationCountdownColorMode.Solid
            ? _countdownColorMode
            : (int)NotificationCountdownColorMode.Gradient;
        _countdownGradientColors = NotificationCountdownPalette.NormalizeColors(_countdownGradientColors);
        _countdownSolidColor = NotificationCountdownPalette.ToOpaque(_countdownSolidColor);

        _legacyAutoCloseTime = null;
        _legacyUseBuiltInOnClassIslandFailure = null;
    }

    partial void OnDisplayDurationChanging(int value) => _hasDisplayDuration = true;
}

public partial class OverridableNotificationChannelSettings : NotificationChannelSettings
{
    [ObservableProperty] private bool _overrideBasicSettings;
    [ObservableProperty] private bool _overrideNotificationWindowSettings;
    [ObservableProperty] private bool _overrideServiceSettings;

    public void EnableAllOverrides()
    {
        OverrideBasicSettings = true;
        OverrideNotificationWindowSettings = true;
        OverrideServiceSettings = true;
    }

    public void DisableAllOverrides()
    {
        OverrideBasicSettings = false;
        OverrideNotificationWindowSettings = false;
        OverrideServiceSettings = false;
    }

    public static OverridableNotificationChannelSettings CreateEnabled()
    {
        var settings = new OverridableNotificationChannelSettings();
        settings.SetDefaultEnabled(true);
        settings.OverrideBasicSettings = true;
        return settings;
    }
}

/// <summary>内置通知浮窗的倒计时样式。</summary>
public enum NotificationCountdownStyle
{
    /// <summary>结果区的文字秒数提示（旧行为）。</summary>
    Text = 0,

    /// <summary>沿浮窗四周收缩的描边（默认）。</summary>
    Edge = 1
}

/// <summary>倒计时描边的配色模式。</summary>
public enum NotificationCountdownColorMode
{
    Gradient = 0,
    Solid = 1
}
