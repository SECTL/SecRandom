using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Models.SubConfigs;

public partial class FloatingWindowSettingsConfig : ObservableObject
{
    [ObservableProperty] private bool _startupDisplayFloatingWindow = true;

    /// <summary>
    ///     在屏幕左右两侧各显示一扇悬浮窗。开启后主窗钉在它原本那一侧、另一侧补一扇副窗，
    ///     两扇窗分别钉在各自边缘，只能纵向移动。
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
    ///     另一侧副窗的纵向锚点（屏幕坐标里窗口中心 Y）。为 <c>null</c> 时副窗跟随主窗的纵向位置。
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

    // 收纳手柄的“文字”样式：空字符串等于沿用本地化默认文字（中文“抽”、英文“Go”）。
    // 允许的语言与长度由 FloatingWindowDockHandle 决定，非中文语言不限“一个字”。
    [ObservableProperty] private string _stickToEdgeDisplayText = string.Empty;

    // 收纳手柄的“图标”样式：图标字形为空时回退到默认头像图标，要恢复默认就把这个值清空
    [ObservableProperty] private string _stickToEdgeDisplayIconGlyph = string.Empty;

    // 收纳手柄的自定义图片文件名，与 FloatingWindowIconService 管理的 PNG 同名
    [ObservableProperty] private string _stickToEdgeDisplayIconImage = string.Empty;
    [ObservableProperty] private bool _draggable = true;
    [ObservableProperty] private int _floatingWindowSize = 56;
    [ObservableProperty] private int _longPressDuration = 500;

    /// <summary>
    ///     闪抽按钮支持长按快选：按住时按钮四周走一圈蓝→绿进度环，按住满
    ///     <see cref="LongPressDuration" /> 毫秒后浮窗收起成球、左侧滑出 1–5 人快选滑轨，
    ///     松手即按选中的头像开抽。关掉后闪抽按钮回到"点一下抽 1 人"的老行为。
    /// </summary>
    [ObservableProperty] private bool _enableQuickDrawLongPress = true;
    [ObservableProperty] private bool _doNotStealFocus = true;
    [ObservableProperty] private bool _hideOnForeground = false;
    [ObservableProperty] private string _hideOnForegroundWindowTitles = string.Empty;
    [ObservableProperty] private string _hideOnForegroundProcessNames = string.Empty;
}
