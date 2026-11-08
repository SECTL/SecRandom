using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Notification;

namespace SecRandom.Core.Models.SubConfigs;

/// <summary>
///     全屏结果通知渠道的外观、文案、动画与倒计时设置。
/// </summary>
/// <remarks>
///     <para>
///         这是**渠道自己的**窗口外观，刻意不复用 <see cref="NotificationChannelSettings.Transparency" />
///         或浮窗的位置/透明度字段：内置通知渠道是 QuickDraw 窗口，而全屏渠道是另一扇独立窗口，
///         两者的外观语义不同。根 <c>AGENTS.md</c> 禁止"通知设置复制 QuickDraw 窗口外观控件"，
///         全屏渠道不复制浮窗外观，但确实拥有自己的外观字段。
///     </para>
///     <para>
///         子配置的属性变化不会自动落盘（<c>ConfigHandlerBase</c> 只订阅根模型的属性变化），
///         设置页统一在 <c>NotificationChannelSettingsPageBase.SettingsOnPropertyChanged</c>
///         末尾调用 <c>ConfigHandler.Save()</c>。
///     </para>
/// </remarks>
public partial class FullscreenNotificationSettings : ObservableObject
{
    /// <summary>背景模糊半径的下限。</summary>
    public const int MinBackgroundBlurRadius = 0;

    /// <summary>背景模糊半径的上限。</summary>
    public const int MaxBackgroundBlurRadius = 64;

    /// <summary>结果字体大小的下限（像素）。</summary>
    public const int MinResultFontSize = 32;

    /// <summary>结果字体大小的上限（像素）。</summary>
    public const int MaxResultFontSize = 240;

    /// <summary>结果字体默认大小（像素）。</summary>
    public const int DefaultResultFontSize = 96;

    /// <summary>文案单条的字符上限，与导入校验共用。</summary>
    public const int MaxTextLength = 200;

    /// <summary>打字机速度下限（字/秒）。</summary>
    public const int MinTypewriterCharactersPerSecond = 4;

    /// <summary>打字机速度上限（字/秒）。</summary>
    public const int MaxTypewriterCharactersPerSecond = 40;

    /// <summary>打字机默认速度（字/秒）：约 12 字/秒，接近正常阅读节奏。</summary>
    public const int DefaultTypewriterCharactersPerSecond = 12;

    /// <summary>渐变角度的下限（度）。</summary>
    public const int MinBackgroundGradientAngle = 0;

    /// <summary>渐变角度的上限（度）。</summary>
    public const int MaxBackgroundGradientAngle = 360;

    /// <summary>默认渐变角度：135° 从左上往右下，和主题底色渐变同向。</summary>
    public const int DefaultBackgroundGradientAngle = 135;

    /// <summary>纯色模式默认色：近黑的深灰，投影在教室白墙上不刺眼。</summary>
    public static readonly Color DefaultBackgroundColor = Color.Parse("#FF101418");

    /// <summary>渐变默认起点色（深海军蓝）。</summary>
    public static readonly Color DefaultBackgroundGradientStart = Color.Parse("#FF1E3A5F");

    /// <summary>渐变默认终点色（近黑）。</summary>
    public static readonly Color DefaultBackgroundGradientEnd = Color.Parse("#FF0B1220");

    /// <summary>是否显示文案（前后段）。只在打字机样式下生效，其余样式直接展示自己的结果画面。</summary>
    [ObservableProperty] private bool _showText = true;

    /// <summary>全屏呈现是否语音播报文案整句。默认关，避免与抽取设置里的语音播报叠加。</summary>
    [ObservableProperty] private bool _showVoiceAnnouncement;

    private FullscreenAnimationStyle _animationStyle = FullscreenAnimationStyle.Typewriter;

    /// <summary>
    ///     动画样式，默认打字机。
    /// </summary>
    /// <remarks>
    ///     手写 setter 而不是 <c>[ObservableProperty]</c> 字段：已移除的转盘在旧
    ///     <c>settings.json</c>/备份里是数字 <c>2</c>，反序列化会直接落进这个 setter。
    ///     未定义的枚举值一律归一到学号网格（与原转盘同样是"候选池排布 + 定格赢家"的呈现），
    ///     否则窗口 switch 会掉进 default 的静态结果，旧用户升级后就像动画设置被悄悄关掉了。
    /// </remarks>
    public FullscreenAnimationStyle AnimationStyle
    {
        get => _animationStyle;
        set => SetProperty(
            ref _animationStyle,
            Enum.IsDefined(value) ? value : FullscreenAnimationStyle.Grid);
    }

    /// <summary>
    ///     打字机速度（字/秒）。默认 <see cref="DefaultTypewriterCharactersPerSecond" />；
    ///     动画时长由文案长度与这个速度共同决定，不再有总时长上限，文案绝不会被截断。
    /// </summary>
    [ObservableProperty] private int _typewriterCharactersPerSecond = DefaultTypewriterCharactersPerSecond;

    /// <summary>背景来源，默认跟随主题。</summary>
    [ObservableProperty] private FullscreenBackgroundMode _backgroundMode = FullscreenBackgroundMode.Theme;

    /// <summary>纯色模式的底色（只在 <see cref="FullscreenBackgroundMode.Solid" /> 下生效）。</summary>
    [ObservableProperty] private Color _backgroundColor = DefaultBackgroundColor;

    /// <summary>双色渐变模式的起点色（只在 <see cref="FullscreenBackgroundMode.Gradient" /> 下生效）。</summary>
    [ObservableProperty] private Color _backgroundGradientStart = DefaultBackgroundGradientStart;

    /// <summary>双色渐变模式的终点色（只在 <see cref="FullscreenBackgroundMode.Gradient" /> 下生效）。</summary>
    [ObservableProperty] private Color _backgroundGradientEnd = DefaultBackgroundGradientEnd;

    /// <summary>双色渐变的角度（度，0 = 从左往右，顺时针转）。</summary>
    [ObservableProperty] private int _backgroundGradientAngle = DefaultBackgroundGradientAngle;

    /// <summary>
    ///     背景图文件名（相对 <c>data/images/fullscreen/</c>）。空字符串表示用内置底色兜底。
    /// </summary>
    [ObservableProperty] private string _backgroundImage = "";

    /// <summary>背景图模糊半径（像素）。</summary>
    [ObservableProperty] private int _backgroundBlurRadius = 12;

    /// <summary>背景层不透明度（百分比）。</summary>
    [ObservableProperty] private int _backgroundOpacity = 100;

    /// <summary>结果里是否包含序号。默认只要名称（用户 m03502）。</summary>
    [ObservableProperty] private bool _showResultNumber;

    /// <summary>结果里是否包含名称。</summary>
    [ObservableProperty] private bool _showResultName = true;

    /// <summary>结果里是否带头像（只有配了图片的成员才有，缺失时安静跳过）。</summary>
    [ObservableProperty] private bool _showResultAvatar;

    /// <summary>结果字体大小（像素）。多结果时按可用空间向下收缩，不会被这个值撑破版面。</summary>
    [ObservableProperty] private int _resultFontSize = DefaultResultFontSize;

    /// <summary>是否显示剩余人数。</summary>
    [ObservableProperty] private bool _showRemainingCount = true;

    /// <summary>强调色是否跟随应用主题色（默认跟随）。关闭后才使用 <see cref="AccentColor" />。</summary>
    [ObservableProperty] private bool _useThemeAccentColor = true;

    /// <summary>自定义强调色（<c>#RRGGBB</c>），仅在 <see cref="UseThemeAccentColor" /> 关闭时生效。</summary>
    [ObservableProperty] private string _accentColor = "";

    /// <summary>实际生效的模糊半径（夹回合法区间）。</summary>
    public int EffectiveBackgroundBlurRadius =>
        Math.Clamp(BackgroundBlurRadius, MinBackgroundBlurRadius, MaxBackgroundBlurRadius);

    /// <summary>实际生效的结果字体大小（夹回合法区间）。</summary>
    public int EffectiveResultFontSize =>
        Math.Clamp(ResultFontSize, MinResultFontSize, MaxResultFontSize);

    /// <summary>实际生效的打字机速度（字/秒，夹回合法区间）。</summary>
    public int EffectiveTypewriterCharactersPerSecond => Math.Clamp(
        TypewriterCharactersPerSecond,
        MinTypewriterCharactersPerSecond,
        MaxTypewriterCharactersPerSecond);

    /// <summary>实际生效的背景层不透明度（0..100）。</summary>
    public int EffectiveBackgroundOpacity => Math.Clamp(BackgroundOpacity, 0, 100);

    /// <summary>实际生效的渐变角度（0..360）。</summary>
    public int EffectiveBackgroundGradientAngle =>
        Math.Clamp(BackgroundGradientAngle, MinBackgroundGradientAngle, MaxBackgroundGradientAngle);

    /// <summary>
    ///     读盘后把背景相关的字段收拾一遍：模式折成合法值、角度夹进 0..360、图片文件名去空白。
    /// </summary>
    /// <remarks>
    ///     从 settings.json 手工改出来的越界值不该让整屏画歪；正常设置页写入的值本来就合法，
    ///     这一步只是兜底（与看板类项目的 <c>NormalizeBackground</c> 同一职责）。
    /// </remarks>
    public void NormalizeBackground()
    {
        if (!Enum.IsDefined(BackgroundMode))
            BackgroundMode = FullscreenBackgroundMode.Theme;

        BackgroundGradientAngle = Math.Clamp(
            BackgroundGradientAngle,
            MinBackgroundGradientAngle,
            MaxBackgroundGradientAngle);

        BackgroundBlurRadius = Math.Clamp(
            BackgroundBlurRadius,
            MinBackgroundBlurRadius,
            MaxBackgroundBlurRadius);

        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100);
        BackgroundImage = BackgroundImage?.Trim() ?? string.Empty;

        // 字号也在这里夹回区间：settings.json 被手改过时，页面和窗口都不该拿到越界值。
        ResultFontSize = Math.Clamp(ResultFontSize, MinResultFontSize, MaxResultFontSize);
    }

    /// <summary>
    ///     把当前配置算成渲染层能直接用的样子（模式归一化、角度夹取、按模式挑出该用的颜色）。
    ///     <para>
    ///         解析失败或未选图一律退回 <see cref="FullscreenBackgroundAppearance.Theme" />：
    ///         一块坏配置不该把整屏画成透明的。
    ///     </para>
    /// </summary>
    public FullscreenBackgroundAppearance ResolveBackgroundAppearance()
    {
        return BackgroundMode switch
        {
            FullscreenBackgroundMode.Solid => FullscreenBackgroundAppearance.Theme with
            {
                Mode = FullscreenBackgroundMode.Solid,
                SolidColor = BackgroundColor
            },
            FullscreenBackgroundMode.Gradient => FullscreenBackgroundAppearance.Theme with
            {
                Mode = FullscreenBackgroundMode.Gradient,
                GradientStart = BackgroundGradientStart,
                GradientEnd = BackgroundGradientEnd,
                GradientAngle = EffectiveBackgroundGradientAngle
            },
            FullscreenBackgroundMode.Image => FullscreenBackgroundAppearance.Theme with
            {
                Mode = FullscreenBackgroundMode.Image
            },
            _ => FullscreenBackgroundAppearance.Theme
        };
    }
}
