using System.Collections.Generic;
using System.ComponentModel;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;
using SecRandom.Core.Behaviors;
using SecRandom.Core.Controls;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Helpers.UI;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Core.Services.SimpleMode;
using SecRandom.Helpers;
using SecRandom.Services.FloatingWindow;
using SecRandom.Services.Linkage;
using SecRandom.Services;
using SecRandom.Services.Platform;
using SecRandom.Services.ViewEngine;
using SecRandom.Platforms.Abstractions;
using SecRandom.ViewModels;

namespace SecRandom.Views;

public partial class FloatingWindow : Window
{
    private bool _isMovingWindow;
    private bool _isPendingWindowDrag;
    private bool _isNativeWindowDrag;
    private PixelPoint _nativeWindowDragStartPosition;
    private bool _isDocked;
    private bool _isDockedOnLeft;
    private int _dockRevision;
    private int _snapAnimationRevision;
    private bool _isMovingDockHandle;
    private bool _dockHandleWasDragged;
    private PixelPoint _dockDragStartScreenPoint;
    private PixelPoint _dockDragStartPosition;
    private PixelPoint _windowDragStartScreenPoint;
    private PixelPoint _windowDragStartPosition;
    private Screen? _windowDragScreen;
    private bool _windowDragStartedByMouse;
    private PixelRect? _dockWorkingArea;
    private int _dockTransitionRevision;
    private bool _isDockTransitioning;
    private int _expandedWindowWidth;
    private int _expandedWindowHeight;
    private int _dockAnchorCenterY;
    private readonly bool _isSecondary;
    private bool _edgePinned;
    private bool _pinnedOnLeft;
    private readonly CourseLinkageService _linkageService = IAppHost.GetService<CourseLinkageService>();
    private readonly IFeatureAvailabilityService _featureAvailability = IAppHost.GetService<IFeatureAvailabilityService>();
    private bool _hiddenByCourseLinkage;
    private bool _wasVisibleBeforeCourseLinkage;
    private bool _userWantsVisible = true;
    private bool _isClosed;

    /// <summary>
    ///     窗口正在按 <see cref="CanClose" /> 被关闭。关闭流程会先把 <see cref="Window.IsVisible" /> 置为 false
    ///     再触发 <c>Closed</c>，所以这时 <see cref="_isClosed" /> 还是 false，不能把这次"不可见"当成用户收起悬浮窗。
    /// </summary>
    private bool _isClosing;

    /// <summary>窗口是否被真正显示过。没显示过的窗口在布局初始化里改可见性不该反过来去藏另一侧。</summary>
    private bool _hasBeenShown;

    /// <summary>首次显示后补断言置顶的延迟，避开窗口还没真正落到目标屏的瞬间。</summary>
    private const int TopmostReassertDelayMilliseconds = 160;

    /// <summary>
    ///     保存位置与屏幕边缘的距离在这个像素范围内就算贴边。吸附动画和"启动时是否直接进入收纳态"
    ///     必须用同一个阈值，否则会出现"上次退出时明明是贴边收纳的球、这次启动却摊开展开态"。
    /// </summary>
    private const int EdgeSnapDistance = 36;

    /// <summary>鼠标按下后移动多少像素才算真正开始拖窗（沿用原来的精确阈值）。</summary>
    private const int MouseDragStartDistance = 4;

    /// <summary>触控/手写笔按下后移动多少像素才算开始拖窗：手指的抖动需要更大的死区，否则点按会被吞。</summary>
    private const int TouchDragStartDistance = 16;

    /// <summary>首次呈现时的定位只判断一次：之后窗口再显示不应该重新被当成"上次是收纳态"。</summary>
    private bool _startupPositionRestored;

    /// <summary>
    ///     首次呈现时按"上次是收纳球"处理：在 <see cref="OnLoaded" /> 里先把窗口透明，
    ///     等首帧布局量出真实的展开尺寸后再判断贴边并直接落成球。
    /// </summary>
    private bool _startupDockPending;

    public FloatingWindow() : this(isSecondary: false)
    {
    }

    /// <summary>
    ///     创建一扇悬浮窗。
    /// </summary>
    /// <param name="isSecondary">
    ///     为 <c>true</c> 时是"左右两侧显示"开启后补在另一侧的那扇窗；主窗则留在它原本所在的那一侧。
    ///     副窗与主窗共用同一套按钮、停靠与联动逻辑，但只保存自己的纵向锚点：横向位置永远由角色
    ///     （主窗在哪侧、副窗就在另一侧）决定，不写共享的 <see cref="FloatPositionConfig" />，
    ///     否则两扇窗会互相覆盖位置。
    /// </param>
    public FloatingWindow(bool isSecondary)
    {
        _isSecondary = isSecondary;
        DataContext = this;
        Position = new PixelPoint(ViewModel.Config.FloatPosition.X, ViewModel.Config.FloatPosition.Y);
        InitializeComponent();
        // 背景材质在 ApplyWindowBorderOpacity 中按设置切换；构造期先请求透明，
        // 这样窗口创建时不会先闪出一块默认的系统底色。
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        this.ApplyPlatformFeatures(WindowFeatures.ToolWindow, enabled: true);
        ApplyFocusFreeMode();

        UiRenderQuality.Apply(this);

        Closing += OnClosing;
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        ViewModel.Config.FloatingWindowSettings.PropertyChanged += FloatingWindowSettings_OnPropertyChanged;
        ViewModel.Config.LinkageSettings.PropertyChanged += LinkageSettings_OnPropertyChanged;
        _linkageService.StateChanged += LinkageServiceOnStateChanged;
        _featureAvailability.Changed += FeatureAvailabilityOnChanged;
        var buttonRegistry = IAppHost.TryGetService<IFloatingWindowButtonRegistry>();
        if (buttonRegistry is not null)
            buttonRegistry.Changed += FloatingWindowButtonRegistry_OnChanged;
        Opened += OnOpened;
        PropertyChanged += OnWindowPropertyChanged;
        SizeChanged += OnWindowSizeChanged;
        if (Application.Current is { } application)
            application.ActualThemeVariantChanged += OnActualThemeVariantChanged;
        Closed += (_, _) =>
        {
            _isClosed = true;
            _quickPickRestoring = false;
            PropertyChanged -= OnWindowPropertyChanged;
            SizeChanged -= OnWindowSizeChanged;
            _featureAvailability.Changed -= FeatureAvailabilityOnChanged;
            if (buttonRegistry is not null)
                buttonRegistry.Changed -= FloatingWindowButtonRegistry_OnChanged;
            if (Application.Current is { } closedApplication)
                closedApplication.ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        };
        
        var settings = EffectiveFloatingWindowSettings;
        ButtonsPanel.Children.Add(GetStartingButton(settings));
        Dispatcher.UIThread.Post(RefreshItems);
    }

    private void FloatingWindowButtonRegistry_OnChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshItems);
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e)
    {
        // 卡片底是 DynamicResource 的实色画刷，主题切换由资源自己刷新；这里只需重算一次不透明度。
        ApplyWindowBorderOpacity(EffectiveFloatingWindowSettings);
    }

    /// <summary>
    ///     窗口重新显示时补一次原生置顶：托盘"显示悬浮窗"、课程联动恢复、副窗创建都走
    ///     <see cref="Window.Show" /> 而不是设置应用，不会经过 <see cref="ApplyWindowSettings" />。
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty)
            return;

        if (IsVisible)
        {
            Dispatcher.UIThread.Post(ReassertNativeTopmost, DispatcherPriority.Render);
            return;
        }

        // 两侧同时显示时，一扇藏起来另一扇也得跟着藏，否则会剩半扇飘在桌面上。
        // 课程联动自己会同时藏两侧（它先把 _hiddenByCourseLinkage 置位再 Hide），这里不能抢跑，
        // 否则另一侧的"联动前是否可见"会被记成 false，下课后就回不来了。
        // 关窗同理：Avalonia 先置 IsVisible = false 再触发 Closed，若把关闭当成收起，关掉副窗就会把
        // 留在原本那一侧的主窗一起藏掉（关掉"两侧同时显示"后两扇窗全不见了）。
        if (_hasBeenShown && !_isClosing && !_hiddenByCourseLinkage)
            App.NotifyFloatingWindowHidden(this);
    }

    /// <summary>
    ///     另一侧被操作时把自己收成球：两侧同时显示时只留一侧展开，别让两扇窗一起抢注意力。
    /// </summary>
    public void RequestAutoDock()
    {
        if (!IsVisible
            || _isClosed
            || _isDocked
            || _isDockTransitioning
            || _isMovingDockHandle
            || IsQuickPickActive
            || !EffectiveFloatingWindowSettings.StickToEdge)
            return;

        _ = CollapseToDockAsync();
    }

    /// <summary>
    ///     窗口高度变化后重新对齐两侧。按钮列表刚换好、语言/字号变化都会让 <c>SizeToContent</c>
    ///     重新量一遍，量完的瞬间另一侧还停在旧锚点上，看起来就是"两扇窗高度不一样，拖一下才统一"。
    /// </summary>
    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_isClosed || !IsLoaded)
            return;

        // 快选期间窗口尺寸就是那块"球 + 滑轨"面板，和展开态没有可比性：这时候去对齐两侧，
        // 会把正在快选的这扇窗按另一侧的锚点重新摆一次，用户看到的就是长按后浮窗上下抽搐。
        if (IsQuickPickActive)
            return;

        var settings = EffectiveFloatingWindowSettings;
        if (!settings.ShowOnBothSides || !settings.SyncBothSidesAnchor)
            return;

        App.RequestFloatingWindowAnchorAlignment();
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public bool CanClose { get; set; } = false;
    public bool UserWantsVisible => _userWantsVisible;
    public bool IsHiddenByCourseLinkage => _hiddenByCourseLinkage;
    public bool IsSecondary => _isSecondary;
    public bool IsEdgePinned => _edgePinned;

    /// <summary>快选面板（按住/滑选）是否正开着：这段时间窗口的尺寸和位置都属于面板，别人不能重摆。</summary>
    internal bool IsQuickPickActive => _quickPickState != QuickPickState.Idle || _quickPickRestoring;

    /// <summary>
    ///     窗口此刻所在的横向半区：<c>true</c> 表示所在屏幕的左半区。
    /// </summary>
    /// <remarks>
    ///     "左右两侧同时显示"靠它决定主窗留在哪一侧：用户原本把单扇浮窗放在左边，开启两侧显示后
    ///     主窗就不该跳到右边，否则关掉开关后窗口会停在与原来相反的一侧。钉边/停靠状态优先，
    ///     因为这两种状态下窗口已经被强制贴在自己那一侧；其余情况按窗口中心与该屏工作区中心比较，
    ///     与停靠判定用的是同一条中线规则。
    /// </remarks>
    public bool IsOnLeftSide
    {
        get
        {
            if (_edgePinned)
                return _pinnedOnLeft;

            if (_isDocked)
                return _isDockedOnLeft;

            var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
            var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
            var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
            if (workingArea is not { } area)
                return false;

            return Position.X + width / 2 <= area.Center.X;
        }
    }

    /// <summary>当前窗口中心的屏幕 Y 坐标；停靠态用停靠锚点。</summary>
    public int AnchorCenterY
    {
        get
        {
            if (_isDocked && _dockAnchorCenterY > 0)
                return _dockAnchorCenterY;

            var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
            return Position.Y + height / 2;
        }
    }

    /// <summary>
    ///     浮窗实际使用的设置。简洁模式下隐藏了浮窗设置页，因此改读一份内存里的默认设置，
    ///     用户原来的浮窗设置不会被改写，关掉简洁模式后原样恢复。
    /// </summary>
    private FloatingWindowSettingsConfig EffectiveFloatingWindowSettings =>
        _featureAvailability.IsSimpleModeEnabled ? SimpleModeDefaults.FloatingWindowSettings : ViewModel.Config.FloatingWindowSettings;

    private bool IsSimpleMode => _featureAvailability.IsSimpleModeEnabled;

    /// <summary>
    ///     钉住/解除钉住窗口的左右边缘。
    /// </summary>
    /// <param name="pinned">是否钉边。</param>
    /// <param name="onLeft">钉在哪一侧；解除钉住时忽略。</param>
    /// <remarks>
    ///     钉边是"左右两侧显示"的交互约束：开启后主窗钉在它原本那一侧、副窗钉在另一侧，
    ///     用户只能纵向移动，不能被拖到屏幕中间或换到另一侧。解除钉住后恢复原来的吸附/自由拖动行为。
    ///     主窗留在自己原本那一侧（而不是固定右侧），这样关掉"左右两侧同时显示"后窗口才回得到原来那一侧。
    /// </remarks>
    public void SetEdgePinned(bool pinned, bool onLeft)
    {
        var changed = _edgePinned != pinned || (pinned && _pinnedOnLeft != onLeft);
        _edgePinned = pinned;
        if (!pinned)
            return;

        _pinnedOnLeft = onLeft;
        _isDockedOnLeft = onLeft;
        UpdateDockButton();
        if (!IsLoaded)
            return;

        if (_isDocked)
            RepositionDockedWindow();
        else
            PinExpandedWindowToEdge();

        if (changed)
            SavePosition();
    }

    /// <summary>
    ///     把本窗的纵向位置对齐到另一侧传过来的锚点（"两侧高度统一"开启时使用）。
    /// </summary>
    /// <remarks>
    ///     只调整位置、不触发保存：拖动过程中的实时跟随不应该反复写配置，保存仍由拖动结束统一完成。
    /// </remarks>
    public void ApplyUnifiedAnchor(int anchorCenterY)
    {
        if (_isClosed)
            return;

        // 快选把本窗挪到了指针旁边、尺寸也换成了面板：此时按锚点重新摆放会把面板从指针下拽走。
        if (IsQuickPickActive)
            return;

        var settings = EffectiveFloatingWindowSettings;
        if (!settings.ShowOnBothSides || !settings.SyncBothSidesAnchor)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is not { } area)
            return;

        _dockWorkingArea = area;
        _dockAnchorCenterY = Math.Clamp(
            anchorCenterY,
            area.Y + height / 2,
            Math.Max(area.Y + height / 2, area.Bottom - height / 2));

        if (_isDocked)
            RepositionDockedWindow();
        else if (_edgePinned && !_isDockTransitioning)
            PositionExpandedWindowAtDockAnchor(useCurrentSize: true);
    }

    /// <summary>把本窗的纵向锚点同步给另一侧的悬浮窗（只在"两侧高度统一"开启时）。</summary>
    private void NotifySiblingAnchor(int anchorCenterY)
    {
        if (!EffectiveFloatingWindowSettings.SyncBothSidesAnchor)
            return;

        App.SyncFloatingWindowAnchor(this, anchorCenterY);
    }

    public void RefreshItems()
    {
        if (App.Current.IsStopping)
            return;
        
        var settings = EffectiveFloatingWindowSettings;
        ApplyWindowSettings(settings);
        ButtonsPanel.Children.Clear();
        foreach (var controlName in GetVisibleButtonNames(settings, _featureAvailability.IsLotteryEnabled))
        {
            var control = controlName switch
            {
                "roll_call" => GetRollCallButton(settings),
                "quick_draw" => GetQuickDrawButton(settings),
                "lottery" => GetLotteryButton(settings),
                "timer" => GetTimerButton(settings),
                _ => null
            };

            if (control != null)
                ButtonsPanel.Children.Add(control);
        }

        if (!IsSimpleMode)
        {
            AddCustomButtons(settings);

            foreach (var button in GetVisiblePluginButtons(settings))
                ButtonsPanel.Children.Add(CreatePluginButton(button, settings));
        }

        UpdateButtonsPanelWidth(settings);
    }

    private void AddCustomButtons(FloatingWindowSettingsConfig settings)
    {
        var iconService = IAppHost.TryGetService<FloatingWindowIconService>();
        foreach (var config in FloatingWindowButtonCatalog.GetRenderableButtons(settings.CustomButtons))
        {
            var label = FloatingWindowButtonCatalog.TryGetDisplayLabel(config.Label, out var displayLabel)
                ? displayLabel
                : config.Label;
            var bitmap = iconService?.TryLoadBitmap(config.IconImage);
            var button = CreateButton(config.IconGlyph, label, settings, bitmap);
            var captured = config;
            button.Click += (_, _) => ExecuteCustomButton(captured);
            ButtonsPanel.Children.Add(button);
        }
    }

    private void ExecuteCustomButton(FloatingWindowButtonConfig config)
    {
        if (FloatingWindowButtonActionRunner.TryExecute(config, out var error))
            return;

        var message = error == FloatingWindowButtonExecutionError.InvalidTarget
            ? Langs.SettingsPages.Personalized.FloatingWindowButtons.Resources.M_InvalidTarget
            : Langs.SettingsPages.Personalized.FloatingWindowButtons.Resources.M_OpenFailed;
        this.ShowErrorToast(message);
    }

    private IReadOnlyList<FloatingWindowButtonDescriptor> GetVisiblePluginButtons(FloatingWindowSettingsConfig settings)
    {
        var registry = IAppHost.GetService<IFloatingWindowButtonRegistry>();
        var registered = registry.Buttons;

        var staleIds = settings.VisiblePluginButtonIds
            .Where(id => registered.All(button => !string.Equals(button.Id, id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (staleIds.Length > 0)
        {
            foreach (var staleId in staleIds)
                settings.VisiblePluginButtonIds.Remove(staleId);
            IAppHost.TryGetService<MainConfigHandler>()?.Save();
        }

        return registered
            .Where(button => settings.VisiblePluginButtonIds.Contains(button.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private static Button CreatePluginButton(FloatingWindowButtonDescriptor descriptor, FloatingWindowSettingsConfig settings)
    {
        var button = CreateButton(descriptor.Icon, descriptor.Label, settings);
        button.Click += (_, _) => descriptor.Click();
        return button;
    }

    private void ApplyWindowSettings(FloatingWindowSettingsConfig settings)
    {
        ApplyWindowBorderOpacity(settings);

        // 顺序是刻意的：免激活样式要先落盘，因为平台层刷新扩展样式时会重新排一次 z 序；
        // 置顶必须最后写，否则会被随后的样式刷新抢回普通层。设置实时切换、简洁模式切换、
        // 配置重新载入都会回到这里，所以无焦点模式必须在这里重新应用——窗口已经显示时
        // 只改 ShowActivated 不会生效，必须让平台层补上/去掉免激活样式。
        ApplyFocusFreeMode();

        var topmost = settings.FloatingWindowTopmostMode is TopmostMode.Topmost or TopmostMode.UiAccess;
        Topmost = topmost;
        if (IsLoaded)
            this.ApplyPlatformFeatures(WindowFeatures.Topmost, topmost);
        ButtonsPanel.Orientation = settings.FloatingWindowPlacement == 1
            ? Orientation.Vertical
            : Orientation.Horizontal;
        ButtonsPanel.Width = double.NaN;

        if (!settings.StickToEdge && _isDocked)
            RestoreFromDock();
        else if (_isDocked)
        {
            UpdateDockButton();
            Dispatcher.UIThread.Post(() => RepositionDockedWindow(), DispatcherPriority.Render);
        }

        // 钉边窗口的宽度会随按钮/字号变化，展开状态下要把左/右坐标重新贴回边缘。
        if (_edgePinned && !_isDocked && IsLoaded)
            Dispatcher.UIThread.Post(PinExpandedWindowToEdge, DispatcherPriority.Render);

        if (topmost)
            ReassertNativeTopmost();
    }

    /// <summary>应用浮窗不透明度，底色由主题资源提供。</summary>
    private void ApplyWindowBorderOpacity(FloatingWindowSettingsConfig settings)
    {
        WindowBorder.Opacity = Math.Clamp(settings.FloatingWindowOpacity, 20, 100) / 100.0;
    }

    /// <summary>
    ///     应用"无焦点模式"：开启后点击悬浮窗不会把焦点从前台应用（例如正在播放的 PPT）抢走，
    ///     翻页笔因此可以继续翻页。
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="Window.ShowActivated" /> 只决定窗口首次 <c>Show()</c> 时是否激活，用户点击
    ///         窗口时仍会激活，所以这里同时向平台层请求 <see cref="WindowFeatures.NoActivate" />
    ///         （Windows 上是 <c>WS_EX_NOACTIVATE</c>）。该样式只影响激活，不吞鼠标事件，
    ///         悬浮窗的按钮、拖动与长按都不受影响。
    ///     </para>
    ///     <para>
    ///         平台没有免激活能力时（Linux/macOS）退化为只保留 <c>ShowActivated=false</c>：显示时
    ///         不抢焦点，但点击仍会激活。这正是设置文案"尽量"的含义，而不是静默失败。
    ///     </para>
    /// </remarks>
    private void ApplyFocusFreeMode()
    {
        var focusFree = EffectiveFloatingWindowSettings.DoNotStealFocus;
        ShowActivated = !focusFree;
        this.ApplyPlatformFeatures(WindowFeatures.NoActivate, focusFree);
    }

    /// <summary>
    ///     向平台窗口服务再请求一次原生置顶（Windows 上是 <c>HWND_TOPMOST</c> 的 <c>SetWindowPos</c>）。
    /// </summary>
    /// <remarks>
    ///     <see cref="Window.Topmost" /> 只写窗口管理器标志。首次显示、样式刷新、从停靠态还原、
    ///     课程联动恢复这些时机，浮窗都可能被压在别的窗户（包括任务栏这类本身置顶的窗户）后面，
    ///     所以这些时机都补一次原生置顶。平台不支持时静默忽略，绝不影响浮窗交互。
    /// </remarks>
    private void ReassertNativeTopmost()
    {
        if (_isClosed || !IsVisible)
            return;

        var settings = EffectiveFloatingWindowSettings;
        if (settings.FloatingWindowTopmostMode is not (TopmostMode.Topmost or TopmostMode.UiAccess))
            return;

        try
        {
            var handle = TryGetPlatformHandle();
            if (handle is null || handle.Handle == nint.Zero)
                return;

            var service = IAppHost.TryGetService<IWindowFeatureService>();
            service?.Apply(
                new PlatformWindowHandle(handle.Handle, handle.HandleDescriptor),
                new WindowFeatureRequest(WindowFeatures.Topmost, true));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"悬浮窗：请求原生置顶失败：{ex.Message}");
        }
    }

    private static int GetButtonSize(int value)
    {
        return value <= 6
            ? value switch { 0 => 28, 1 => 32, 2 => 40, 3 => 48, 4 => 56, 5 => 64, _ => 72 }
            : Math.Clamp(value, 32, 160);
    }

    private static int GetEffectiveButtonSize(FloatingWindowSettingsConfig settings)
    {
        return GetButtonSize(settings.FloatingWindowSize);
    }

    private IEnumerable<string> GetVisibleButtonNames(FloatingWindowSettingsConfig settings, bool isLotteryEnabled)
    {
        // 简洁模式回归抽取本身：保留点名与闪抽，抽奖与计时器按钮不显示
        if (IsSimpleMode)
        {
            yield return "roll_call";
            yield return "quick_draw";
            yield break;
        }

        if (settings.ShowRollCallButton) yield return "roll_call";
        if (settings.ShowQuickDrawButton) yield return "quick_draw";
        if (settings.ShowLotteryButton && isLotteryEnabled) yield return "lottery";
        if (settings.ShowTimerButton) yield return "timer";
    }

    private void FeatureAvailabilityOnChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshItems);
    }

    private void FloatingWindowSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 副窗锚点在每次纵向拖动结束后都会写回，它不是可见配置：重建一遍按钮没有意义，
        // 也不该打断正在进行的停靠动画。"两侧高度统一"的开关只影响位置对齐，由 App 统一处理。
        if (e.PropertyName is nameof(FloatingWindowSettingsConfig.SecondaryAnchorCenterY)
            or nameof(FloatingWindowSettingsConfig.SyncBothSidesAnchor))
            return;

        RefreshItems();
    }

    private void LinkageSettings_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyCourseLinkageVisibility();
    }

    private void LinkageServiceOnStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ApplyCourseLinkageVisibility);
    }

    public void SetUserVisibilityIntent(bool visible)
    {
        App.NotifyQuickPickVisibilityIntentChanged(this, visible);
        _userWantsVisible = visible;
        if (visible && _hiddenByCourseLinkage)
            _wasVisibleBeforeCourseLinkage = true;
        if (!visible)
            _wasVisibleBeforeCourseLinkage = false;
    }

    private void ApplyCourseLinkageVisibility()
    {
        var linkageSettings = _linkageService.Settings;
        var shouldHide = (linkageSettings.HideFloatingWindowOnClassEnd
                && _linkageService.IsConfirmedBreakTime)
            || (linkageSettings.HideFloatingWindowOnOutdoorCourse
                && _linkageService.IsOutdoorCourseOnClass);
        if (shouldHide == _hiddenByCourseLinkage)
            return;

        _hiddenByCourseLinkage = shouldHide;
        if (shouldHide)
        {
            _wasVisibleBeforeCourseLinkage = IsVisible;
            if (IsVisible)
                Hide();
            return;
        }

        if (_userWantsVisible && _wasVisibleBeforeCourseLinkage)
        {
            App.RestoreWithoutActivating(this);

            // 课程联动恢复时窗口是重新 Show() 出来的，部分后端会丢掉置顶标志，这里补一次。
            Dispatcher.UIThread.Post(ReassertNativeTopmost, DispatcherPriority.Render);
        }

        _wasVisibleBeforeCourseLinkage = false;
    }

    private static Button GetStartingButton(FloatingWindowSettingsConfig settings)
    {
        return CreateButton(FluentIcons.ArrowClockwiseRegular, Langs.Common.Resources.Feat_Starting, settings);
    }

    private static Button GetRollCallButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton(FluentIcons.PeopleFilled, Langs.Common.Resources.Feat_RollCall, settings);

        b.Click += (sender, args) =>
        {
            App.ToggleMainWindow("main.rollCall");
        };

        return b;
    }

    private Button GetQuickDrawButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton(FluentIcons.FlashFilled, Langs.Common.Resources.Feat_QuickDraw, settings);
        b.Name = QuickDrawButtonName;

        b.Click += (sender, args) =>
        {
            App.ShowQuickDrawWindow(noticeAnchor: this);
        };

        return b;
    }

    private static Button GetLotteryButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton(FluentIcons.GiftFilled, Langs.Common.Resources.Feat_Lottery, settings);

        b.Click += (sender, args) =>
        {
            App.ToggleMainWindow("main.lottery");
        };

        return b;
    }

    private static Button GetTimerButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton(FluentIcons.TimerFilled, Langs.Common.Resources.Feat_Timer, settings);
        b.Click += (_, _) => _ = IAppHost.GetService<TimerViewService>().ShowAsync();
        return b;
    }

    private static Button CreateButton(
        string icon,
        string label,
        FloatingWindowSettingsConfig settings,
        Bitmap? image = null)
    {
        var size = GetEffectiveButtonSize(settings);
        var displayStyle = settings.FloatingWindowDisplayStyle;
        var padding = new Thickness(Math.Max(2, size * 0.08));
        var button = new Button
        {
            Height = size,
            Width = GetButtonWidth(size, label, displayStyle, padding),
            Margin = new Thickness(2),
            Padding = padding,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

        button.Content = displayStyle switch
        {
            1 => CreateIconVisual(icon, image, size * 0.64),
            2 => new TextBlock
            {
                Text = label,
                FontSize = Math.Max(10, size * 0.28),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            },
            _ => new StackPanel
            {
                Orientation = Orientation.Vertical,
                Spacing = Math.Max(1, size * 0.04),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    CreateIconVisual(icon, image, size * 0.46),
                    new TextBlock
                    {
                        Text = label,
                        FontSize = Math.Max(8, size * 0.16),
                        TextAlignment = TextAlignment.Center,
                        TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };

        return button;
    }

    private static Control CreateIconVisual(string icon, Bitmap? image, double iconSize)
    {
        if (image is null)
            return new FluentIcon(icon, iconSize);

        return new Image
        {
            Source = image,
            Width = iconSize,
            Height = iconSize,
            Stretch = Stretch.Uniform
        };
    }

    private void UpdateButtonsPanelWidth(FloatingWindowSettingsConfig settings)
    {
        var buttons = ButtonsPanel.Children
            .OfType<Button>()
            .ToArray();
        if (buttons.Length == 0)
        {
            ButtonsPanel.Width = double.NaN;
            return;
        }

        var buttonWidth = buttons.Max(button => button.Width);
        foreach (var button in buttons)
            button.Width = buttonWidth;

        var rowCount = settings.FloatingWindowPlacement switch
        {
            0 => Math.Min(2, buttons.Length),
            2 => buttons.Length,
            _ => 1
        };
        ButtonsPanel.Width = settings.FloatingWindowPlacement == 0
            ? (buttonWidth + buttons[0].Margin.Left + buttons[0].Margin.Right) * rowCount
            : double.NaN;
    }

    private static double GetButtonWidth(int size, string label, int displayStyle, Thickness padding)
    {
        if (displayStyle == 1)
            return size;

        var fontSize = displayStyle == 2
            ? Math.Max(10, size * 0.28)
            : Math.Max(9, size * 0.22);
        var textWidth = label.Sum(character => character switch
        {
            ' ' => fontSize * 0.35,
            >= '\u2E80' => fontSize,
            _ => fontSize * 0.62
        });
        return Math.Max(size, Math.Ceiling(textWidth + padding.Left + padding.Right + 2));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (CanClose || e.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            // 先于 IsVisible 的变更记下"这是关闭而不是收起"，否则 OnWindowPropertyChanged 会去藏另一侧。
            _isClosing = true;
            ViewModel.Config.FloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
            ViewModel.Config.LinkageSettings.PropertyChanged -= LinkageSettings_OnPropertyChanged;
            _linkageService.StateChanged -= LinkageServiceOnStateChanged;

            // 快选中途关窗（托盘退出/重启）也要把让位的那一侧交还，否则它会一直藏到下次启动。
            App.NotifyQuickPickFinished(this);
            return;
        }

        e.Cancel = true;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyWindowSettings(EffectiveFloatingWindowSettings);
        if (!_startupPositionRestored)
        {
            // 先不透明地等首帧：此刻 Bounds 还是 0/设计尺寸，拿它去量展开宽高、算贴边距离都会
            // 得到错的结论（右侧贴边尤其明显）。真正的判断在首帧布局完成后由
            // RestoreStartupPositionAndScheduleDock 按真实尺寸做，判断结果不是球时再把窗口亮回来。
            _startupDockPending = ShouldRestoreDockedAtStartup();
            if (_startupDockPending)
                Opacity = 0;
            Dispatcher.UIThread.Post(RestoreStartupPositionAndScheduleDock, DispatcherPriority.Render);
        }

        // 触发布局更新
        Width = 20;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _hasBeenShown = true;
        this.ApplyPlatformFeatures(WindowFeatures.SkipTaskSwitcher, enabled: true);
        ApplyFocusFreeMode();

        // 部分后端在窗口首次显示时会丢掉构造期设置的置顶标志；显示后立刻补一次，
        // 再等窗口真正落到目标屏、尺寸生效后补第二次。
        ReassertNativeTopmost();
        DispatcherTimer.RunOnce(ReassertNativeTopmost, TimeSpan.FromMilliseconds(TopmostReassertDelayMilliseconds));
    }

    private async void RestoreStartupPositionAndScheduleDock()
    {
        if (_startupPositionRestored)
            return;

        _startupPositionRestored = true;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));

        // 上次退出时浮窗已经是收纳球（保存位置贴着屏幕边缘、且开启了自动收纳）：这次直接以球出现，
        // 不再先摊开展开态、等 StickToEdgeRecoverSeconds 秒后才收回去。
        if (_startupDockPending)
        {
            _startupDockPending = false;
            if (TryResolveStartupDock(width, height, out var dockedOnLeft))
            {
                _isDockedOnLeft = dockedOnLeft;
                await RestoreStartupDockedWindowAsync();
                return;
            }

            // 保存位置并不贴边：窗口本来就不该收成球，把首帧前藏起来的展开态亮回来。
            Opacity = 1;
        }

        if (_edgePinned)
        {
            RestorePinnedStartupPosition(width, height);
            return;
        }

        if (IsFullyVisibleOnAnyScreen(Position, width, height))
        {
            ScheduleDockIfAtEdge();
            return;
        }

        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        CaptureExpandedWindowSize();
        _isDockedOnLeft = Position.X + width / 2 <= workingArea.Value.Center.X;
        _dockWorkingArea = workingArea.Value;
        _dockAnchorCenterY = Math.Clamp(
            Position.Y + height / 2,
            workingArea.Value.Y + height / 2,
            Math.Max(workingArea.Value.Y + height / 2, workingArea.Value.Bottom - height / 2));
        await ShowStartupDockedWindowAsync();
    }

    /// <summary>
    ///     这次启动是否可能要以收纳球出现：只有开启"贴边自动收纳"且恢复秒数不为 0 时，
    ///     贴边的保存位置才代表上次是收纳态。秒数为 0 是"拖到边缘也一直展开"，绝不能强制收成球。
    /// </summary>
    private bool ShouldRestoreDockedAtStartup()
    {
        var settings = EffectiveFloatingWindowSettings;
        return settings.StickToEdge && Math.Clamp(settings.StickToEdgeRecoverSeconds, 0, 60) > 0;
    }

    /// <summary>
    ///     首帧布局完成后判断保存位置是不是贴边的收纳态，并把停靠所需的状态一次准备好。
    /// </summary>
    /// <remarks>
    ///     必须等布局完成再调用：这里要拿窗口真实的展开宽高去算距左右边缘的距离，也要把它记成
    ///     展开尺寸，供之后"从球展开"和 <see cref="SavePosition" /> 还原展开边界。
    /// </remarks>
    private bool TryResolveStartupDock(int width, int height, out bool dockedOnLeft)
    {
        dockedOnLeft = false;
        if (!ShouldRestoreDockedAtStartup())
            return false;

        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is not { } area)
            return false;

        if (_edgePinned)
        {
            // 钉边窗口永远贴在自己那一侧；启动坐标可能还是开启"两侧同时显示"之前的旧位置。
            dockedOnLeft = _pinnedOnLeft;
        }
        else if (!TryResolveEdgeDock(area, width, out dockedOnLeft))
        {
            return false;
        }

        CaptureExpandedWindowSize();
        _dockWorkingArea = area;
        _dockAnchorCenterY = Math.Clamp(
            _dockAnchorCenterY,
            area.Y + _expandedWindowHeight / 2,
            Math.Max(area.Y + _expandedWindowHeight / 2, area.Bottom - _expandedWindowHeight / 2));
        return true;
    }

    /// <summary>
    ///     上次退出时是收纳态：按保存的边缘与纵向锚点直接落成球，不再先展开再等它收回去。
    /// </summary>
    private async Task RestoreStartupDockedWindowAsync()
    {
        var settings = EffectiveFloatingWindowSettings;

        if (_edgePinned)
            _isDockedOnLeft = _pinnedOnLeft;

        // 两侧高度统一时纵向锚点是两扇窗共用的：主窗也读副窗锚点，否则只拖动过副窗时
        // 下次启动两侧又会错开。统一锚点对收纳态同样有效（保存的就是窗口中心的 Y）。
        var unifiedAnchor = settings.ShowOnBothSides && settings.SyncBothSidesAnchor;
        if ((_isSecondary || unifiedAnchor) && settings.SecondaryAnchorCenterY is { } savedAnchor)
            _dockAnchorCenterY = savedAnchor;

        var expandedHeight = _expandedWindowHeight > 0
            ? _expandedWindowHeight
            : Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _dockWorkingArea
            ?? GetScreenForWindow(Position)?.WorkingArea;
        if (workingArea is { } area)
        {
            _dockWorkingArea = area;
            _dockAnchorCenterY = Math.Clamp(
                _dockAnchorCenterY,
                area.Y + expandedHeight / 2,
                Math.Max(area.Y + expandedHeight / 2, area.Bottom - expandedHeight / 2));
        }

        await ShowStartupDockedWindowAsync();
    }

    /// <summary>
    ///     把窗口直接以收纳球的状态呈现：不播收起动画，先把整个窗口透明，等 SizeToContent
    ///     量完球的大小、贴到边缘后再显示，因此屏幕上不会闪过一帧展开态。
    /// </summary>
    private async Task ShowStartupDockedWindowAsync()
    {
        _isDocked = true;
        Opacity = 0;
        ExpandedContent.IsVisible = false;
        DockButton.Opacity = 1;
        UpdateDockButton();

        try
        {
            // SizeToContent 要等一帧才会把窗口量成球的大小，这时算贴边位置才不会用成展开尺寸。
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            if (_isClosed)
                return;

            RepositionDockedWindow();
        }
        finally
        {
            // 定位过程里任何提前返回都不能把窗口留在透明状态：启动时为了让展开态不闪一帧才先置
            // Opacity = 0，漏掉还原就等于浮窗启动后彻底看不见。
            if (!_isClosed)
                Opacity = 1;
        }

        SavePosition();
    }

    /// <summary>保存的位置是否贴着屏幕左/右边缘；贴着就同时给出它属于哪一侧。</summary>
    private bool TryResolveEdgeDock(PixelRect workingArea, int width, out bool onLeft)
    {
        var distanceToLeft = Math.Abs(Position.X - workingArea.X);
        var distanceToRight = Math.Abs(workingArea.Right - (Position.X + width));
        onLeft = distanceToLeft <= distanceToRight;
        return Math.Min(distanceToLeft, distanceToRight) <= EdgeSnapDistance;
    }

    /// <summary>
    ///     钉边窗口的启动定位：横向直接贴到角色对应的边缘，纵向用记住的锚点。
    /// </summary>
    /// <remarks>
    ///     不能走"完整可见就只安排吸附"那条分支：钉边窗口本来就在边缘上，但它可能被
    ///     从另一侧拖过来（开启功能前的位置），所以这里每次都按角色重算横坐标并恢复纵向锚点。
    /// </remarks>
    private void RestorePinnedStartupPosition(int width, int height)
    {
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is not { } area)
            return;

        var settings = EffectiveFloatingWindowSettings;

        // 两侧高度统一时纵向锚点是两扇窗共用的：主窗也读副窗锚点，否则只拖动过副窗时
        // 下次启动主窗会回到旧的 FloatPosition，两侧就又错开了。
        var unifiedAnchor = settings.ShowOnBothSides && settings.SyncBothSidesAnchor;
        var savedAnchorCenterY = _isSecondary || unifiedAnchor
            ? settings.SecondaryAnchorCenterY
            : null;
        CaptureExpandedWindowSize();
        _isDockedOnLeft = _pinnedOnLeft;
        _dockWorkingArea = area;
        _dockAnchorCenterY = Math.Clamp(
            savedAnchorCenterY ?? Position.Y + height / 2,
            area.Y + height / 2,
            Math.Max(area.Y + height / 2, area.Bottom - height / 2));
        PositionExpandedWindowAtDockAnchor(useCurrentSize: true);
        SavePosition();
        if (settings.StickToEdge)
            ScheduleDock();
    }

    /// <summary>把展开状态的钉边窗口重新贴回它那一侧。</summary>
    private void PinExpandedWindowToEdge()
    {
        if (!_edgePinned || _isDocked || _isDockTransitioning)
            return;

        // 快选面板不是"展开态"，钉边的横向计算会把整块面板从指针下横着推开。
        if (IsQuickPickActive)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is not { } area)
            return;

        _dockWorkingArea = area;
        Position = new PixelPoint(
            _isDockedOnLeft ? area.X : area.Right - width,
            Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // 无焦点模式下窗口不会被点击激活，Windows 也就不会在点击时把它抬回置顶窗口带的最上层，
        // 于是别的置顶窗口（任务栏、ClassIsland 主窗、其它常驻置顶工具）会一直压在它上面，
        // 看起来就像"开了无焦点模式置顶就失效"。这里在按下时手动抬一次：既保留免激活，
        // 又不丢置顶效果。
        ReassertNativeTopmost();

        // 每一次按下都从"这次按下还没有被快选取消"开始：上一次长按被拖窗取消时留下的抑制标记
        // 只对那一次松手有效，不能带到下一次按键上，否则这次按下别的按钮时松手会被吞掉、按钮点不动。
        _quickPickTapSuppressed = false;

        // 快选面板已经开着时又按了一下：那是"确认"而不是拖窗，按当前高亮人数收尾。
        if (TryHandleQuickPickPointerPressed(e))
        {
            e.Handled = true;
            return;
        }

        var isPrimaryContact = IsPrimaryContact(e);

        // 鼠标右键同样算一次长按：Windows 把触控"长按"转成的就是这次右键按下
        //（详见 TryBeginQuickDrawLongPress 的说明）。只认主指针的话，触控长按会整个失效。
        var isSecondaryContact = e.Pointer.Type == PointerType.Mouse
                                 && e.GetCurrentPoint(this).Properties.IsRightButtonPressed;

        // 闪抽按钮支持长按快选：这里只开始计时并画环，刻意不接管指针，短按仍走按钮自己的 Click。
        if ((isPrimaryContact || isSecondaryContact) && TryBeginQuickDrawLongPress(e, e.Source as Visual))
            return;

        // Wait until a press is known not to be a quick-pick gesture. On Windows a touch
        // long-press is promoted to a second right-button press; collapsing the sibling on
        // the first touch press made it collapse and then hide again.
        App.NotifyFloatingWindowInteraction(this);

        if (EffectiveFloatingWindowSettings.Draggable
            && isPrimaryContact)
        {
            var source = e.Source as Control;

            if (_isDocked && IsDockButtonChild(source))
            {
                ++_dockRevision;
                _isMovingDockHandle = true;
                _dockHandleWasDragged = false;
                _dockDragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this));
                _dockDragStartPosition = Position;
                e.Pointer.Capture(this);
                return;
            }

            // Linux/macOS window managers own the top-level move loop. Starting it from the
            // original press lets Wayland and AppKit move the window without position polling.
            if (App.UsesNativeWindowDrag
                && e.Pointer.Type == PointerType.Mouse
                && !_isDocked
                && !_edgePinned)
            {
                ++_snapAnimationRevision;
                ++_dockRevision;
                _isPendingWindowDrag = true;
                _isMovingWindow = false;
                _isNativeWindowDrag = true;
                _nativeWindowDragStartPosition = Position;
                BeginMoveDrag(e);
                return;
            }

            ++_snapAnimationRevision;
            _isPendingWindowDrag = true;
            _isMovingWindow = false;
            // 记住这次按下是鼠标还是手指：手指的抖动比鼠标大得多，拖动阈值与"丢松手"兜底
            // 都只对真正是鼠标的那次按下生效，避免触控拖动被抬起键的兜底逻辑提前收尾。
            _windowDragStartedByMouse = e.Pointer.Type == PointerType.Mouse;
            _windowDragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this));
            _windowDragStartPosition = Position;
            _windowDragScreen = GetScreenForWindow(Position);
        }
    }

    /// <summary>
    ///     这次按下是不是"主指针"：鼠标左键，或触控/手写笔的按下。
    /// </summary>
    /// <remarks>
    ///     触控按下没有鼠标按键状态可读，只按 <c>IsLeftButtonPressed</c> 判断会让手指按在浮窗上
    ///     完全没有反应（拖不动、点不动收纳球、长按也起不来）。指针类型优先，与仓库里
    ///     <c>AcceleratingRepeatButton</c> 的判定保持一致。
    /// </remarks>
    private bool IsPrimaryContact(PointerPressedEventArgs e)
    {
        return e.Pointer.Type is PointerType.Touch or PointerType.Pen
            || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        // 快选面板打开后这次指针已经被它接管，绝不能再当成拖窗。
        if (TryHandleQuickPickPointerMoved(e))
            return;

        if (_isNativeWindowDrag)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                _isMovingWindow = Position != _nativeWindowDragStartPosition;
                CompleteWindowDrag(e.Pointer);
            }
            return;
        }

        // 松手事件被别的路径吞掉时（快选取消、触控被系统收走），拖动状态会一直挂着，
        // 鼠标之后只是悬停也会继续拖着窗口走。这次拖动确实由鼠标开始时，键已抬起就把它收尾。
        if (_isPendingWindowDrag
            && _windowDragStartedByMouse
            && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            CompleteWindowDrag(e.Pointer);
            return;
        }

        if (_isMovingDockHandle)
        {
            MoveDockHandle(e);
            return;
        }

        if (!_isPendingWindowDrag)
            return;

        var pointerPosition = Position + ToPixelPoint(e.GetPosition(this));
        var deltaX = pointerPosition.X - _windowDragStartScreenPoint.X;
        var deltaY = pointerPosition.Y - _windowDragStartScreenPoint.Y;
        // 手指按下时的自然抖动比鼠标大：阈值太小会把"点一下"当成拖窗，按钮的 Click 就被吞掉了。
        var dragStartDistance = _windowDragStartedByMouse ? MouseDragStartDistance : TouchDragStartDistance;
        if (!_isMovingWindow && Math.Abs(deltaX) < dragStartDistance && Math.Abs(deltaY) < dragStartDistance)
            return;

        if (!_isMovingWindow)
        {
            _isMovingWindow = true;
            ++_dockRevision;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
        if (_edgePinned)
        {
            Position = GetPinnedWindowPosition(_windowDragStartPosition.Y + deltaY);
            var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
            _dockAnchorCenterY = Position.Y + height / 2;

            // 拖动钉边窗口时另一侧跟着走，两扇窗始终同一高度（"两侧高度统一"可关）。
            NotifySiblingAnchor(_dockAnchorCenterY);
            return;
        }

        Position = ConstrainDragPosition(
            new PixelPoint(
                _windowDragStartPosition.X + deltaX,
                _windowDragStartPosition.Y + deltaY),
            pointerPosition);
    }

    private void MoveDockHandle(PointerEventArgs e)
    {
        var workingArea = GetScreenForWindow(Position)?.WorkingArea;
        if (workingArea is null)
            return;

        var pointerPosition = Position + ToPixelPoint(e.GetPosition(this));
        var deltaY = pointerPosition.Y - _dockDragStartScreenPoint.Y;
        // 触控手指按下时的自然抖动比鼠标大：阈值太小会把"点一下收纳球"当成拖动，
        // 按钮的 Click 被吞掉、用户要多点几次。鼠标/触控笔保持原 2px 的灵敏度。
        var dragThreshold = e.Pointer.Type is PointerType.Touch or PointerType.Pen
            ? TouchDragStartDistance
            : MouseDragStartDistance;
        _dockHandleWasDragged |= Math.Abs(deltaY) > dragThreshold;
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var y = Math.Clamp(
            _dockDragStartPosition.Y + deltaY,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        Position = new PixelPoint(_dockDragStartPosition.X, y);
        _dockAnchorCenterY = y + height / 2;

        // 拖动停靠小球时把纵向锚点同步给另一侧，保持"两侧高度统一"。
        NotifySiblingAnchor(_dockAnchorCenterY);
    }

    /// <summary>钉边窗口拖动时只允许改变纵向位置，横向始终贴回角色对应的边缘。</summary>
    private PixelPoint GetPinnedWindowPosition(int requestedY)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _windowDragScreen?.WorkingArea
            ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is not { } area)
            return new PixelPoint(Position.X, requestedY);

        return new PixelPoint(
            _isDockedOnLeft ? area.X : area.Right - width,
            Math.Clamp(requestedY, area.Y, Math.Max(area.Y, area.Bottom - height)));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (TryHandleQuickPickPointerReleased(e))
        {
            e.Handled = true;
            return;
        }

        if (_isMovingDockHandle)
        {
            _isMovingDockHandle = false;
            e.Pointer.Capture(null);
            if (_dockHandleWasDragged)
                SavePosition();
            else
                RestoreFromDock();

            e.Handled = true;
            return;
        }

        if (!_isPendingWindowDrag)
            return;

        if (_isNativeWindowDrag)
        {
            var moved = Position != _nativeWindowDragStartPosition;
            _isNativeWindowDrag = false;
            if (!moved)
            {
                _isPendingWindowDrag = false;
                _isMovingWindow = false;
                return;
            }

            _isMovingWindow = true;
            e.Handled = true;
            CompleteWindowDrag(e.Pointer);
            return;
        }

        // 还没真正拖动就松手 = 这是一次普通点击：不置 Handled，按钮自己的 Click 才会照常触发。
        // 拖动状态不能在这里提前清掉：CompleteWindowDrag 靠 _isMovingWindow 判断这次拖动是否
        // 真的发生过，先清掉会让它直接返回，贴边吸附与自动收纳都不会再排上。
        var wasMoving = _isMovingWindow;
        _isPendingWindowDrag = false;
        if (!wasMoving)
            return;

        e.Handled = true;
        CompleteWindowDrag(e.Pointer);
    }

    /// <summary>
    ///     收尾一次"按下后可能已经拖起来了"的窗口拖动：清掉进行中的拖动状态、交还指针捕获，
    ///     再按钉边/贴边设置落位。
    /// </summary>
    /// <remarks>
    ///     松手被别的逻辑接管（长按快选改判成拖窗后取消）、或拖动中丢了按下状态时也必须走这里。
    ///     以前那些路径只吞掉松手事件、不清理 <c>_isPendingWindowDrag</c>/<c>_isMovingWindow</c>，
    ///     结果松手后窗口还挂在拖动状态里，鼠标只是划过去它就一直跟着指针走。
    /// </remarks>
    private void CompleteWindowDrag(IPointer pointer)
    {
        _isPendingWindowDrag = false;
        _isNativeWindowDrag = false;
        var wasMoving = _isMovingWindow;
        _isMovingWindow = false;
        pointer.Capture(null);

        if (!wasMoving)
            return;

        if (_edgePinned)
        {
            Position = GetPinnedWindowPosition(Position.Y);
            SavePosition();

            // 保存后再同步一次，确保另一侧落到的位置与刚刚写回的锚点一致。
            NotifySiblingAnchor(AnchorCenterY);
            if (EffectiveFloatingWindowSettings.StickToEdge)
                ScheduleDock();
            return;
        }

        if (EffectiveFloatingWindowSettings.StickToEdge)
            _ = SnapToNearestEdgeAsync();
        else
            SavePosition();
    }

    private async Task SnapToNearestEdgeAsync()
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
        {
            SavePosition();
            return;
        }

        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > EdgeSnapDistance)
        {
            SavePosition();
            return;
        }

        _isDockedOnLeft = distanceToLeft <= distanceToRight;
        _dockWorkingArea = workingArea.Value;
        var targetX = _isDockedOnLeft
            ? workingArea.Value.X
            : workingArea.Value.Right - width;
        var targetY = Math.Clamp(
            Position.Y,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        // 低配模式不播吸附动画：直接落到贴边位置，再像动画结束时那样保存并排收纳。
        if (UiMotion.IsReduced)
        {
            Position = new PixelPoint(targetX, targetY);
            SavePosition();
            ScheduleDock();
            return;
        }

        var start = Position;
        var animationRevision = ++_snapAnimationRevision;

        const int durationMilliseconds = 180;
        var frameMilliseconds = AnimationFramePolicy.FrameIntervalMs;
        for (var elapsed = 0; elapsed < durationMilliseconds; elapsed += frameMilliseconds)
        {
            await Task.Delay(frameMilliseconds);
            if (animationRevision != _snapAnimationRevision)
                return;

            var progress = Math.Min(1.0, (elapsed + frameMilliseconds) / (double)durationMilliseconds);
            var eased = 1 - Math.Pow(1 - progress, 3);
            Position = new PixelPoint(
                (int)Math.Round(start.X + (targetX - start.X) * eased),
                (int)Math.Round(start.Y + (targetY - start.Y) * eased));
        }

        if (animationRevision != _snapAnimationRevision)
            return;

        Position = new PixelPoint(targetX, targetY);
        SavePosition();
        ScheduleDock();
    }

    private void MoveToDockedEdge(PixelRect workingArea, int width, int height)
    {
        var x = _isDockedOnLeft
            ? workingArea.X
            : workingArea.Right - width;
        var y = Math.Clamp(
            _dockAnchorCenterY - height / 2,
            workingArea.Y,
            Math.Max(workingArea.Y, workingArea.Bottom - height));
        Position = new PixelPoint(x, y);
    }

    private void RepositionDockedWindow(bool restoring = false)
    {
        if (!_isDocked && !restoring)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        MoveToDockedEdge(
            workingArea.Value,
            width,
            height);
    }

    private void ScheduleDock()
    {
        if (IsQuickPickActive)
            return;

        var seconds = Math.Clamp(EffectiveFloatingWindowSettings.StickToEdgeRecoverSeconds, 0, 60);
        var dockRevision = ++_dockRevision;
        if (seconds == 0)
            return;

        DispatcherTimer.RunOnce(() =>
        {
            if (dockRevision == _dockRevision
                && !_isDocked
                && !IsQuickPickActive
                && EffectiveFloatingWindowSettings.StickToEdge)
                _ = CollapseToDockAsync();
        }, TimeSpan.FromSeconds(seconds));
    }

    private void ScheduleDockIfAtEdge()
    {
        if (IsQuickPickActive || _isDocked || !EffectiveFloatingWindowSettings.StickToEdge)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > EdgeSnapDistance)
            return;

        _isDockedOnLeft = distanceToLeft <= distanceToRight;
        _dockWorkingArea = workingArea.Value;
        ScheduleDock();
    }

    private async Task CollapseToDockAsync()
    {
        if (_isDocked
            || _isDockTransitioning
            || IsQuickPickActive
            || !EffectiveFloatingWindowSettings.StickToEdge)
            return;

        _isDockTransitioning = true;
        try
        {
            var transitionRevision = ++_dockTransitionRevision;
            CaptureExpandedWindowSize();
            _isDocked = true;
            await AnimateControlAsync(
                ExpandedContent,
                1,
                0,
                1,
                0.9,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            Opacity = 0;
            ExpandedContent.IsVisible = false;
            DockButton.Opacity = 0;
            UpdateDockButton();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            RepositionDockedWindow();
            Opacity = 1;
            await AnimateControlAsync(
                DockButton,
                0,
                1,
                0.85,
                1,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision == _dockTransitionRevision)
            {
                SavePosition();
                ReassertNativeTopmost();
            }
        }
        finally
        {
            _isDockTransitioning = false;
        }
    }

    private void UpdateDockButton()
    {
        var settings = EffectiveFloatingWindowSettings;
        var size = Math.Clamp(settings.DockedWindowSize, 28, 96);
        var text = FloatingWindowDockHandle.ResolveText(settings.StickToEdgeDisplayText,
            Langs.Common.Resources.C_PickButtonText);
        var textFontSize = Math.Max(12, size * FloatingWindowDockHandle.GetTextFontScale(text.Length));

        DockButton.Content = settings.StickToEdgeDisplayStyle switch
        {
            // 图标样式：没配字形/图片时回退到原来的头像图标，配了自定义图片就画图片
            FloatingWindowDockHandle.IconStyle => CreateIconVisual(
                string.IsNullOrWhiteSpace(settings.StickToEdgeDisplayIconGlyph)
                    ? FloatingWindowDockHandle.DefaultIconGlyph
                    : settings.StickToEdgeDisplayIconGlyph,
                IAppHost.TryGetService<FloatingWindowIconService>()
                    ?.TryLoadBitmap(settings.StickToEdgeDisplayIconImage),
                size * 0.62),
            // 文字样式：用户自定义（中文一个字、其它语言最多四个），未填写沿用本地化默认文字
            FloatingWindowDockHandle.TextStyle => new TextBlock
            {
                Text = text,
                FontSize = textFontSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            // 箭头样式：用方向图标，不再用 “>” / “<” 字符凑出来
            _ => new FluentIcon(
                _isDockedOnLeft
                    ? FloatingWindowDockHandle.LeftArrowGlyph
                    : FloatingWindowDockHandle.RightArrowGlyph,
                size * 0.62)
        };
        DockButton.Width = size;
        DockButton.Height = size;
        DockButton.Padding = new Thickness(Math.Max(2, size * 0.08));
        DockButton.IsVisible = _isDocked;
    }

    private void DockButton_OnClick(object? sender, RoutedEventArgs e)
    {
        RestoreFromDock();
    }

    private static bool IsDockButtonChild(Visual? visual)
    {
        while (visual != null)
        {
            if (visual is Button { Name: "DockButton" })
                return true;
            visual = visual.GetVisualParent();
        }

        return false;
    }

    private PixelPoint ToPixelPoint(Point point)
    {
        return new PixelPoint(
            (int)Math.Round(point.X * RenderScaling),
            (int)Math.Round(point.Y * RenderScaling));
    }

    private async void RestoreFromDock()
    {
        if (!_isDocked || _isDockTransitioning)
            return;

        _isDockTransitioning = true;
        ++_dockRevision;
        var shouldScheduleDock = false;
        try
        {
            var transitionRevision = ++_dockTransitionRevision;
            _isDocked = false;
            await AnimateControlAsync(
                DockButton,
                DockButton.Opacity,
                0,
                1,
                0.85,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            DockButton.IsVisible = false;
            Opacity = 0;
            PositionExpandedWindowAtDockAnchor();
            ExpandedContent.Opacity = 0;
            ExpandedContent.IsVisible = true;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
            PositionExpandedWindowAtDockAnchor(useCurrentSize: true);
            Opacity = 1;
            await AnimateControlAsync(
                ExpandedContent,
                0,
                1,
                0.9,
                1,
                GetDockTransformOrigin(),
                transitionRevision);
            if (transitionRevision != _dockTransitionRevision)
                return;

            SavePosition();
            ReassertNativeTopmost();
            shouldScheduleDock = true;
        }
        finally
        {
            _isDockTransitioning = false;
        }

        if (shouldScheduleDock)
            ScheduleDock();
    }

    private void CaptureExpandedWindowSize()
    {
        _expandedWindowWidth = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        _expandedWindowHeight = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        _dockAnchorCenterY = Position.Y + _expandedWindowHeight / 2;
    }

    private RelativePoint GetDockTransformOrigin()
    {
        return new RelativePoint(_isDockedOnLeft ? 0 : 1, 0.5, RelativeUnit.Relative);
    }

    private void PositionExpandedWindowAtDockAnchor(bool useCurrentSize = false)
    {
        var width = useCurrentSize
            ? Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling))
            : _expandedWindowWidth;
        var height = useCurrentSize
            ? Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling))
            : _expandedWindowHeight;
        if (width <= 1 || height <= 1)
            return;

        var workingArea = _dockWorkingArea ?? GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        var x = _isDockedOnLeft ? workingArea.Value.X : workingArea.Value.Right - width;
        var y = Math.Clamp(
            _dockAnchorCenterY - height / 2,
            workingArea.Value.Y,
            Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - height));
        Position = new PixelPoint(x, y);
    }

    private PixelPoint ConstrainDragPosition(PixelPoint requestedPosition, PixelPoint pointerPosition)
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var sourceScreen = _windowDragScreen ?? GetScreenForWindow(Position, width, height);
        if (sourceScreen is null)
            return requestedPosition;

        var targetScreen = GetScreenAt(pointerPosition);
        if (targetScreen is not null
            && !ReferenceEquals(sourceScreen, targetScreen)
            && TryConstrainAcrossAdjacentScreens(
                requestedPosition,
                width,
                height,
                sourceScreen.Bounds,
                targetScreen.Bounds,
                sourceScreen.WorkingArea,
                targetScreen.WorkingArea,
                out var constrainedPosition))
        {
            if (IsWithinWorkingArea(constrainedPosition, width, height, targetScreen.WorkingArea))
                _windowDragScreen = targetScreen;

            return constrainedPosition;
        }

        return ClampToWorkingArea(requestedPosition, width, height, sourceScreen.WorkingArea);
    }

    private Screen? GetScreenForWindow(PixelPoint position, int? width = null, int? height = null)
    {
        var center = new PixelPoint(
            position.X + (width ?? Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling))) / 2,
            position.Y + (height ?? Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling))) / 2);
        return GetScreenAt(center)
            ?? GetScreenAt(position)
            ?? Screens.ScreenFromPoint(center)
            ?? Screens.Primary;
    }

    private Screen? GetScreenAt(PixelPoint point)
    {
        foreach (var screen in Screens.All)
        {
            var area = screen.Bounds;
            if (point.X >= area.X
                && point.X < area.Right
                && point.Y >= area.Y
                && point.Y < area.Bottom)
                return screen;
        }

        return null;
    }

    private static bool TryConstrainAcrossAdjacentScreens(
        PixelPoint requestedPosition,
        int width,
        int height,
        PixelRect sourceBounds,
        PixelRect targetBounds,
        PixelRect sourceWorkingArea,
        PixelRect targetWorkingArea,
        out PixelPoint constrainedPosition)
    {
        if (sourceBounds.Right == targetBounds.X || targetBounds.Right == sourceBounds.X)
        {
            var top = Math.Max(sourceWorkingArea.Y, targetWorkingArea.Y);
            var bottom = Math.Min(sourceWorkingArea.Bottom, targetWorkingArea.Bottom);
            if (bottom - top < height)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(
                    requestedPosition.X,
                    Math.Min(sourceWorkingArea.X, targetWorkingArea.X),
                    Math.Max(sourceWorkingArea.Right, targetWorkingArea.Right) - width),
                Math.Clamp(requestedPosition.Y, top, bottom - height));
            return true;
        }

        if (sourceBounds.Bottom == targetBounds.Y || targetBounds.Bottom == sourceBounds.Y)
        {
            var left = Math.Max(sourceWorkingArea.X, targetWorkingArea.X);
            var right = Math.Min(sourceWorkingArea.Right, targetWorkingArea.Right);
            if (right - left < width)
            {
                constrainedPosition = default;
                return false;
            }

            constrainedPosition = new PixelPoint(
                Math.Clamp(requestedPosition.X, left, right - width),
                Math.Clamp(
                    requestedPosition.Y,
                    Math.Min(sourceWorkingArea.Y, targetWorkingArea.Y),
                    Math.Max(sourceWorkingArea.Bottom, targetWorkingArea.Bottom) - height));
            return true;
        }

        constrainedPosition = default;
        return false;
    }

    private static bool IsWithinWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return position.X >= workingArea.X
            && position.X + width <= workingArea.Right
            && position.Y >= workingArea.Y
            && position.Y + height <= workingArea.Bottom;
    }

    private bool IsFullyVisibleOnAnyScreen(PixelPoint position, int width, int height)
    {
        foreach (var screen in Screens.All)
        {
            if (IsWithinWorkingArea(position, width, height, screen.WorkingArea))
                return true;
        }

        return false;
    }

    private static PixelPoint ClampToWorkingArea(PixelPoint position, int width, int height, PixelRect workingArea)
    {
        return new PixelPoint(
            Math.Clamp(position.X, workingArea.X, Math.Max(workingArea.X, workingArea.Right - width)),
            Math.Clamp(position.Y, workingArea.Y, Math.Max(workingArea.Y, workingArea.Bottom - height)));
    }

    private static async Task AnimateControlAsync(
        Control control,
        double fromOpacity,
        double toOpacity,
        double fromScale,
        double toScale,
        RelativePoint transformOrigin,
        int transitionRevision)
    {
        // 低配/关闭动效时直接落终值：连一帧的等待都不占，也不会留下半截动画。
        if (UiMotion.IsReduced)
        {
            control.RenderTransform = null;
            control.Opacity = toOpacity;
            return;
        }

        control.RenderTransformOrigin = transformOrigin;
        var scaleTransform = new ScaleTransform(fromScale, fromScale);
        control.RenderTransform = scaleTransform;
        control.Opacity = fromOpacity;

        const int durationMilliseconds = 150;
        var frameMilliseconds = AnimationFramePolicy.FrameIntervalMs;
        for (var elapsed = 0; elapsed < durationMilliseconds; elapsed += frameMilliseconds)
        {
            await Task.Delay(frameMilliseconds);
            var progress = Math.Min(1.0, (elapsed + frameMilliseconds) / (double)durationMilliseconds);
            var eased = 1 - Math.Pow(1 - progress, 3);
            control.Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
            var scale = fromScale + (toScale - fromScale) * eased;
            scaleTransform.ScaleX = scale;
            scaleTransform.ScaleY = scale;
        }

        control.Opacity = toOpacity;
        scaleTransform.ScaleX = toScale;
        scaleTransform.ScaleY = toScale;
    }

    private void SavePosition()
    {
        // Quick-pick moves this window to a temporary panel location. Persist only real
        // window drags and dock transitions, never the gesture's temporary geometry.
        if (IsQuickPickActive)
            return;

        var settings = EffectiveFloatingWindowSettings;

        // 两侧高度统一时纵向锚点是共用的：任何一侧拖动结束后都写回同一个值，
        // 这样只拖动副窗也能让主窗在下次启动时回到对齐的位置。
        var unifiedAnchor = settings.ShowOnBothSides && settings.SyncBothSidesAnchor;
        var anchorCenterY = AnchorCenterY;

        // 副窗只记住自己的纵向锚点：横向位置由"主窗在哪侧、副窗就在另一侧"这个角色决定，
        // 写共享的 FloatPosition 会让两扇窗在下次启动时抢同一个坐标。
        if (_isSecondary)
        {
            settings.SecondaryAnchorCenterY = anchorCenterY;
            return;
        }

        if (_isDocked && _expandedWindowWidth > 0 && _expandedWindowHeight > 0)
        {
            var workingArea = _dockWorkingArea
                ?? GetScreenForWindow(Position)?.WorkingArea;
            if (workingArea is not null)
            {
                // Persist the expanded bounds so reopening does not interpret a small handle as a full window.
                ViewModel.Config.FloatPosition = new FloatPositionConfig
                {
                    X = _isDockedOnLeft
                        ? workingArea.Value.X
                        : workingArea.Value.Right - _expandedWindowWidth,
                    Y = Math.Clamp(
                        _dockAnchorCenterY - _expandedWindowHeight / 2,
                        workingArea.Value.Y,
                        Math.Max(workingArea.Value.Y, workingArea.Value.Bottom - _expandedWindowHeight))
                };

                if (unifiedAnchor)
                    settings.SecondaryAnchorCenterY = anchorCenterY;
                return;
            }
        }

        ViewModel.Config.FloatPosition = new FloatPositionConfig { X = Position.X, Y = Position.Y };

        if (unifiedAnchor)
            settings.SecondaryAnchorCenterY = anchorCenterY;
    }
}
