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

    /// <summary>首次显示后补断言置顶的延迟，避开窗口还没真正落到目标屏的瞬间。</summary>
    private const int TopmostReassertDelayMilliseconds = 160;

    public FloatingWindow() : this(isSecondary: false)
    {
    }

    /// <summary>
    ///     创建一扇悬浮窗。
    /// </summary>
    /// <param name="isSecondary">
    ///     为 <c>true</c> 时是"左右两侧显示"开启后出现在右边缘的副窗。副窗与主窗共用同一套按钮、
    ///     停靠与联动逻辑，但只保存自己的纵向锚点：横向位置永远由角色决定（主窗在左、副窗在右），
    ///     不写共享的 <see cref="FloatPositionConfig" />，否则两扇窗会互相覆盖位置。
    /// </param>
    public FloatingWindow(bool isSecondary)
    {
        _isSecondary = isSecondary;
        DataContext = this;
        Position = new PixelPoint(ViewModel.Config.FloatPosition.X, ViewModel.Config.FloatPosition.Y);
        InitializeComponent();
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
        Closed += (_, _) =>
        {
            _isClosed = true;
            PropertyChanged -= OnWindowPropertyChanged;
            _featureAvailability.Changed -= FeatureAvailabilityOnChanged;
            if (buttonRegistry is not null)
                buttonRegistry.Changed -= FloatingWindowButtonRegistry_OnChanged;
        };
        
        var settings = EffectiveFloatingWindowSettings;
        ButtonsPanel.Children.Add(GetStartingButton(settings));
        Dispatcher.UIThread.Post(RefreshItems);
    }

    private void FloatingWindowButtonRegistry_OnChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshItems);
    }

    /// <summary>
    ///     窗口重新显示时补一次原生置顶：托盘"显示悬浮窗"、课程联动恢复、副窗创建都走
    ///     <see cref="Window.Show" /> 而不是设置应用，不会经过 <see cref="ApplyWindowSettings" />。
    /// </summary>
    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && IsVisible)
            Dispatcher.UIThread.Post(ReassertNativeTopmost, DispatcherPriority.Render);
    }

    public ViewModelBase ViewModel { get; } = IAppHost.GetService<ViewModelBase>();
    public bool CanClose { get; set; } = false;
    public bool UserWantsVisible => _userWantsVisible;
    public bool IsHiddenByCourseLinkage => _hiddenByCourseLinkage;
    public bool IsSecondary => _isSecondary;
    public bool IsEdgePinned => _edgePinned;

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
    ///     钉边是"左右两侧显示"的交互约束：开启后主窗固定在右边缘、副窗固定在左边缘，
    ///     用户只能纵向移动，不能被拖到屏幕中间或换到另一侧。解除钉住后恢复原来的吸附/自由拖动行为。
    ///     主窗固定在右侧，是因为关掉"左右两侧同时显示"后要保留的就是右侧那扇主窗。
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
        WindowBorder.Opacity = Math.Clamp(settings.FloatingWindowOpacity, 20, 100) / 100.0;

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
        ApplyCustomText(settings, 0);

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

    private static Button GetQuickDrawButton(FloatingWindowSettingsConfig settings)
    {
        var b = CreateButton(FluentIcons.FlashFilled, Langs.Common.Resources.Feat_QuickDraw, settings);

        b.Click += (sender, args) =>
        {
            App.ShowQuickDrawWindow();
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
            ApplyCustomText(settings, 0);
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

        ApplyCustomText(settings, buttonWidth * rowCount);
    }

    // 自定义文字是按钮上方的一行横幅：换行折叠成空格，超长按字符省略；字号随控件尺寸缩放，
    // 宽度以按钮面板为上限，避免长文本把悬浮窗撑开。收纳态下 ExpandedContent 隐藏，文字随之隐藏。
    private void ApplyCustomText(FloatingWindowSettingsConfig settings, double contentWidth)
    {
        var text = (settings.FloatingWindowText ?? string.Empty)
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        var show = settings.EnableFloatingWindowText && text.Length > 0;
        CustomTextBlock.IsVisible = show;
        if (!show)
        {
            CustomTextBlock.Text = string.Empty;
            return;
        }

        CustomTextBlock.Text = text;
        // 0 = 跟随悬浮窗尺寸自动缩放（旧行为）；显式配置时按像素取值，仍夹在可读区间内
        var configuredFontSize = settings.FloatingWindowTextFontSize;
        CustomTextBlock.FontSize = configuredFontSize > 0
            ? Math.Clamp(configuredFontSize, 8, 72)
            : Math.Clamp(settings.FloatingWindowSize * 0.25, 11, 20);
        CustomTextBlock.MaxWidth = Math.Clamp(contentWidth > 1 ? contentWidth : 160, 120, 360);
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
            ViewModel.Config.FloatingWindowSettings.PropertyChanged -= FloatingWindowSettings_OnPropertyChanged;
            ViewModel.Config.LinkageSettings.PropertyChanged -= LinkageSettings_OnPropertyChanged;
            _linkageService.StateChanged -= LinkageServiceOnStateChanged;
            return;
        }

        e.Cancel = true;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ApplyWindowSettings(EffectiveFloatingWindowSettings);
        Dispatcher.UIThread.Post(RestoreStartupPositionAndScheduleDock, DispatcherPriority.Render);
        // 触发布局更新
        Width = 20;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        this.ApplyPlatformFeatures(WindowFeatures.SkipTaskSwitcher, enabled: true);
        ApplyFocusFreeMode();

        // 部分后端在窗口首次显示时会丢掉构造期设置的置顶标志；显示后立刻补一次，
        // 再等窗口真正落到目标屏、尺寸生效后补第二次。
        ReassertNativeTopmost();
        DispatcherTimer.RunOnce(ReassertNativeTopmost, TimeSpan.FromMilliseconds(TopmostReassertDelayMilliseconds));
    }

    private async void RestoreStartupPositionAndScheduleDock()
    {
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
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
        Opacity = 0;
        _isDocked = true;
        ExpandedContent.IsVisible = false;
        DockButton.Opacity = 1;
        UpdateDockButton();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render).GetTask();
        RepositionDockedWindow();
        Opacity = 1;
        SavePosition();
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

        if (EffectiveFloatingWindowSettings.Draggable
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
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

            ++_snapAnimationRevision;
            _isPendingWindowDrag = true;
            _isMovingWindow = false;
            _windowDragStartScreenPoint = Position + ToPixelPoint(e.GetPosition(this));
            _windowDragStartPosition = Position;
            _windowDragScreen = GetScreenForWindow(Position);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
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
        if (!_isMovingWindow && Math.Abs(deltaX) < 4 && Math.Abs(deltaY) < 4)
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
        _dockHandleWasDragged |= Math.Abs(deltaY) > 2;
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

        _isPendingWindowDrag = false;
        if (!_isMovingWindow)
            return;

        _isMovingWindow = false;
        e.Pointer.Capture(null);
        e.Handled = true;
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

        const int snapDistance = 36;
        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > snapDistance)
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
        var seconds = Math.Clamp(EffectiveFloatingWindowSettings.StickToEdgeRecoverSeconds, 0, 60);
        var dockRevision = ++_dockRevision;
        if (seconds == 0)
            return;

        DispatcherTimer.RunOnce(() =>
        {
            if (dockRevision == _dockRevision
                && !_isDocked
                && EffectiveFloatingWindowSettings.StickToEdge)
                _ = CollapseToDockAsync();
        }, TimeSpan.FromSeconds(seconds));
    }

    private void ScheduleDockIfAtEdge()
    {
        if (_isDocked || !EffectiveFloatingWindowSettings.StickToEdge)
            return;

        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling));
        var workingArea = GetScreenForWindow(Position, width, height)?.WorkingArea;
        if (workingArea is null)
            return;

        const int snapDistance = 36;
        var distanceToLeft = Math.Abs(Position.X - workingArea.Value.X);
        var distanceToRight = Math.Abs(workingArea.Value.Right - (Position.X + width));
        if (Math.Min(distanceToLeft, distanceToRight) > snapDistance)
            return;

        _isDockedOnLeft = distanceToLeft <= distanceToRight;
        _dockWorkingArea = workingArea.Value;
        ScheduleDock();
    }

    private async Task CollapseToDockAsync()
    {
        if (_isDocked || _isDockTransitioning || !EffectiveFloatingWindowSettings.StickToEdge)
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
        var style = EffectiveFloatingWindowSettings.StickToEdgeDisplayStyle;
        var size = Math.Clamp(EffectiveFloatingWindowSettings.DockedWindowSize, 28, 96);
        var glyph = _isDockedOnLeft ? ">" : "<";
        DockButton.Content = style switch
        {
            0 => new FluentIcon(FluentIcons.PeopleFilled, size * 0.62),
            1 => new TextBlock
            {
                Text = Langs.Common.Resources.C_PickButtonText,
                FontSize = Math.Max(12, size * 0.42),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            },
            _ => new TextBlock
            {
                Text = glyph,
                FontSize = Math.Max(14, size * 0.52),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
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
        var settings = EffectiveFloatingWindowSettings;

        // 两侧高度统一时纵向锚点是共用的：任何一侧拖动结束后都写回同一个值，
        // 这样只拖动副窗也能让主窗在下次启动时回到对齐的位置。
        var unifiedAnchor = settings.ShowOnBothSides && settings.SyncBothSidesAnchor;
        var anchorCenterY = AnchorCenterY;

        // 副窗只记住自己的纵向锚点：横向位置由"副窗在右"这个角色决定，写共享的 FloatPosition
        // 会让两扇窗在下次启动时抢同一个坐标。
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
