using SecRandom.Core.Models.SubConfigs;

namespace SecRandom.Core.Services.ControlNode;

/// <summary>
///     <c>media.play</c> 里"载荷合法、但本机做不到"的选项。
/// </summary>
/// <remarks>
///     <para>
///         为什么自成一类：这类判定**不是**载荷格式问题（<c>0</c>–<c>100</c> 的整数本身没毛病），
///         而是"这台机器没有这个能力"。写进解析器会把"协议错"和"设备做不到"搅成同一个原因码的两种来源；
///         写在 handler 里又没法单测（handler 要通知服务与 Avalonia 界面）。
///     </para>
///     <para>
///         ⚠️ **系统音量今天是"做不到"，不是"忘了做"**，两条候选路都查过了：
///         <list type="bullet">
///             <item>
///                 <description>
///                     <b>SoundFlow</b>（主项目唯一直接引用的音频库，1.2.1.3）**没有系统音量 API**：
///                     它公开的 <c>Volume</c> 全是应用内组件/工程的增益
///                     （<c>SoundComponent.Volume</c>、<c>Composition.MasterVolume</c>、
///                     <c>AudioSegmentSettings.Volume</c>），<c>AudioPlaybackDevice.MasterMixer</c> 也只是
///                     本应用自己那路输出的混音器音量——改不动 Windows 的端点音量（托盘那个滑块）。
///                     用它顶替 <c>system_volume_percent</c> 会变成一个"看起来调了系统音量"的假实现。
///                 </description>
///             </item>
///             <item>
///                 <description>
///                     <b>NAudio</b> 的 <c>IAudioEndpointVolume</c> 确实能做到，但它**不是**主项目的依赖：
///                     <c>vendors/EdgeTtsSharp/Edge_tts_sharp/Edge_tts_sharp.csproj</c>（带 NAudio 2.0.0）
///                     根本不在解决方案里，主项目只 <c>Compile Include</c> 了它几个源文件，还专门写了
///                     <c>EdgeTtsSharpPlaybackStubs</c> 把 NAudio 的播放路径钉成
///                     <c>NotSupportedException</c>（"播放全部归 SpeechAudioPlayer / MiniAudio"）。
///                     为这一个字段把 NAudio 加回来，与既有架构正好相反。
///                 </description>
///             </item>
///         </list>
///     </para>
///     <para>
///         按 §4.5.7 第 5 条：做不到就**明确拒绝**（<c>invalid_command</c> +
///         <c>result_context { "unsupported_field": "system_volume_percent" }</c>），绝不回成功——
///         静默忽略等于"控制台以为音量调了，教室里根本没变"。
///     </para>
///     <para>
///         要让它走通：在 <c>SecRandom.Platforms.Windows</c> 那类平台层里真的拿到端点音量
///         （自写 CoreAudio interop 或引入 NAudio 的 WASAPI 封装），再把
///         <see cref="SystemVolumeSupported" /> 打开，并在 handler 里补上"播完恢复"。
///         那之前这个字段只能拒——半成品比拒绝更糟。
///     </para>
/// </remarks>
public static class ControlMediaPlayPlatformSupport
{
    /// <summary>系统音量选项的协议字段名（拒绝时原样回给控制台）。</summary>
    public const string SystemVolumeField = ControlMediaPlayRequest.SystemVolumePercentField;

    /// <summary>本机今天能不能临时改系统音量。<c>false</c> = 带该字段的载荷一律拒绝。</summary>
    public static bool SystemVolumeSupported => false;

    /// <summary>
    ///     这条命令里有没有"本机不支持、必须拒绝"的选项？返回字段名；<c>null</c> = 可以执行。
    /// </summary>
    /// <remarks>
    ///     <paramref name="systemVolumeSupported" /> 是参数而不是直接读常量，是为了让
    ///     "平台实现落地之后拒绝必须消失"也能被单测钉住：只有一个恒为 <c>false</c> 的常量，
    ///     测试分不清"判定写对了"和"判定被短路了"。
    /// </remarks>
    public static string? FindUnsupportedField(ControlMediaPlayRequest request, bool systemVolumeSupported)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.SystemVolumePercent is not null && !systemVolumeSupported
            ? SystemVolumeField
            : null;
    }
}

/// <summary>
///     一次播报期间的**临时播报音量**：改内存、播完恢复、绝不落盘。
/// </summary>
/// <remarks>
///     <para>
///         集控 <c>media.play</c> 的 <c>voice_volume_percent</c> 是"这一次播报用这个音量"，
///         不是"把设备的音量改成这个值"。所以这里只动 <see cref="VoiceSettingsConfig.VolumeSize" />
///         这个**内存里的**对象，绝不以任何方式调 <c>Save()</c>；要持久改音量走 <c>settings.write</c>
///         （<c>voice.volume</c>），那是另一条命令（§4.5.7）。
///     </para>
///     <para>
///         ⚠️ 已知边界（如实记下，别当成没有）：语音设置页面**开着**的时候，改 <c>VolumeSize</c> 会触发
///         那个页面的 <c>SettingsOnPropertyChanged</c> → <c>ConfigHandler.Save()</c>
///         （它订阅了 <c>VoiceSettingsConfig.PropertyChanged</c>），于是磁盘上会先落下临时的那个值；
///         恢复时同一个回调会把原值再写回去，所以**净结果不变**，但播放途中进程被杀就会留下临时值。
///         这是既有页面"任何语音设置一改就落盘"的行为，不是这个类引入的；要彻底消除它，
///         得把音量当参数传给 <c>IVoiceAnnouncementService</c>（播报用参数、不碰配置对象），
///         那是一次接口改动，不在本次范围内。
///     </para>
///     <para>
///         为什么是 <see cref="IDisposable" /> 而不是就地赋值：handler 要在 <c>finally</c> 里恢复，
///         而"原值是多少、改的是哪一份"必须由**同一次**改动自己记住，否则异常路径上很容易恢复错值。
///         抽成对象之后，"改了会恢复""没给就一个字不动"都能直接单测。
///     </para>
/// </remarks>
public sealed class TemporaryVoiceVolume : IDisposable
{
    private readonly VoiceSettingsConfig _settings;
    private readonly int? _original;
    private bool _restored;

    private TemporaryVoiceVolume(VoiceSettingsConfig settings, int? original)
    {
        _settings = settings;
        _original = original;
    }

    /// <summary>
    ///     把播报音量临时设成 <paramref name="percent" />（0–100）；
    ///     <c>null</c> = 这次不改，返回的句柄什么都不做。
    /// </summary>
    /// <remarks>
    ///     这里再夹一次范围：载荷层已经校验过 0–100，而 <see cref="VoiceSettingsConfig.VolumeSize" />
    ///     是公开可写属性，将来别处直接调本方法时不该把越界值写进活着的配置对象
    ///     （那会让设置页面显示 500%）。协议层的"越界就整条拒绝"在解析器里，不在这里。
    /// </remarks>
    public static TemporaryVoiceVolume Apply(VoiceSettingsConfig settings, int? percent)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (percent is not { } value)
            return new TemporaryVoiceVolume(settings, null);

        var clamped = Math.Clamp(value, ControlMediaPlayRequest.MinVolumePercent, ControlMediaPlayRequest.MaxVolumePercent);
        var original = settings.VolumeSize;
        settings.VolumeSize = clamped;

        return new TemporaryVoiceVolume(settings, original);
    }

    /// <summary>恢复改动前的值。</summary>
    /// <remarks>
    ///     <para>
    ///         幂等：重复 <see cref="Dispose" /> 不会把音量"恢复"成上一轮的临时值。
    ///     </para>
    ///     <para>
    ///         恢复的是 <b>Apply 那一刻</b>的值（§4.5.7：恢复的是设备当时的值，不是任何默认值）。
    ///         如果播报期间有人在设置页面里手动改了音量，这里会把它覆盖回旧值——按协议这条路径
    ///         "播完无条件恢复"优先，且这段时间只有几秒，可预期比"临时值留在机器上"安全。
    ///     </para>
    ///     <para>
    ///         <c>null</c> 那次改动（载荷根本没给这个字段）在 Apply 里就没碰过任何东西，
    ///         这里也一个字都不动：老载荷的行为必须逐字不变，包括**不触发** <c>PropertyChanged</c>。
    ///     </para>
    /// </remarks>
    public void Dispose()
    {
        if (_restored)
            return;

        _restored = true;
        if (_original is { } original)
            _settings.VolumeSize = original;
    }
}
