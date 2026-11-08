using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class FloatingWindowSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _startupDisplayFloatingWindow = true;

    /// <summary>
    ///     在屏幕左右两侧各显示一扇悬浮窗。开启后两扇窗分别钉在左/右边缘，只能纵向移动。
    /// </summary>
    [ObservableProperty] private bool _showOnBothSides = false;

    /// <summary>
    ///     左右两侧的悬浮窗保持同一高度。开启后拖动任意一侧，另一侧会跟着上下移动。
    /// </summary>
    /// <remarks>
    ///     只在 <see cref="ShowOnBothSides" /> 开启时生效。统一高度时两扇窗共用
    ///     <see cref="SecondaryAnchorCenterY" /> 作为纵向锚点：它由任意一侧拖动后写回，
    ///     这样只拖动副窗也能在下次启动时恢复两侧对齐的纵向位置。
    /// </remarks>
    [ObservableProperty] private bool _syncBothSidesAnchor = true;

    /// <summary>
    ///     右侧副窗的纵向锚点（屏幕坐标里窗口中心 Y）。为 <c>null</c> 时副窗跟随主窗的纵向位置。
    /// </summary>
    /// <remarks>
    ///     用可空整数是为了让"还没记住过"与"记住的是 0"可区分；控制面目录按类型归类，
    ///     <c>int?</c> 本来就不会被描述成远程可写项，副窗位置因此不会暴露给集控。
    /// </remarks>
    [ObservableProperty] private int? _secondaryAnchorCenterY;
    [ObservableProperty] private int _floatingWindowOpacity = 80;
    [ObservableProperty] private TopmostMode _floatingWindowTopmostMode = TopmostMode.Topmost;
    [ObservableProperty] private bool _showRollCallButton = true;
    [ObservableProperty] private bool _showQuickDrawButton = true;
    [ObservableProperty] private bool _showLotteryButton = false;
    [ObservableProperty] private bool _showTimerButton = false;
    [ObservableProperty] private List<string> _visiblePluginButtonIds = [];

    /// <summary>
    ///     用户自定义按钮。渲染顺序为"内置按钮 → 自定义按钮 → 插件按钮"，最多渲染前
    ///     <c>FloatingWindowButtonCatalog.MaxButtons</c> 个；超出的条目保留在配置里，不会静默删除。
    /// </summary>
    /// <remarks>
    ///     子配置的属性变化不会冒泡到 <c>MainConfigModel</c>，因此编辑页必须显式调用
    ///     <c>MainConfigHandler.Save()</c>；给它赋一个新的 <c>List</c> 才能通知悬浮窗刷新。
    /// </remarks>
    [ObservableProperty] private List<FloatingWindowButtonConfig> _customButtons = [];
    [ObservableProperty] private int _floatingWindowPlacement = 1;
    [ObservableProperty] private int _floatingWindowDisplayStyle = 0;
    [ObservableProperty] private int _floatingWindowTheme = 0;
    [ObservableProperty] private bool _stickToEdge = true;
    [ObservableProperty] private int _stickToEdgeRecoverSeconds = 3;
    [ObservableProperty] private int _stickToEdgeDisplayStyle = 1;
    [ObservableProperty] private int _dockedWindowSize = 32;
    [ObservableProperty] private bool _draggable = true;
    [ObservableProperty] private int _floatingWindowSize = 56;
    // 悬浮窗自定义文字：默认关闭；空字符串等于不显示，旧配置缺字段时按默认值加载
    [ObservableProperty] private bool _enableFloatingWindowText;
    [ObservableProperty] private string _floatingWindowText = string.Empty;
    // 自定义文字字号（像素）：0 表示跟随悬浮窗尺寸自动缩放，与按钮字号保持同一比例
    [ObservableProperty] private int _floatingWindowTextFontSize;
    [ObservableProperty] private int _longPressDuration = 500;
    [ObservableProperty] private bool _doNotStealFocus = true;
    [ObservableProperty] private bool _hideOnForeground = false;
    [ObservableProperty] private string _hideOnForegroundWindowTitles = string.Empty;
    [ObservableProperty] private string _hideOnForegroundProcessNames = string.Empty;
}
