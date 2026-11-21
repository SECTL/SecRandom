using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.Picking;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     「抽取方式」和「抽取模式」的分工：抽取方式回答「谁被抽出来」，抽取模式只回答「重复抽取记录怎么处理」。
/// </summary>
/// <remarks>
///     <para>
///         编排抽取没有重复语义——顺序早在抽取前就排定了，重复上限、平均次数保护和公平权重全被绕过——
///         所以它不该挂在「抽取模式」这个重复策略下拉里，而是和随机抽取、公平抽取一样出现在「抽取方式」里。
///         抽取端读的仍然是 <see cref="DrawMode.Orchestrated" />，这里只负责把「选了编排」这件事
///         翻译成那个值，并在退出编排时把重复策略还回去。
///     </para>
///     <para>
///         为什么不在选中编排时直接把 <c>DrawMode</c> 覆盖掉就算了：那样用户再改回随机抽取时，
///         原来的允许重复 / 不重复 / 半重复已经没人记得，半重复的档位会被静默压成不重复。
///         所以进入编排前先把它记进 <see cref="OverridableDrawSettings.SampledDrawMode" />。
///     </para>
/// </remarks>
public static class DrawModeSelection
{
    public static bool IsOrchestrated(DrawMode mode) => mode == DrawMode.Orchestrated;

    /// <summary>选中「编排抽取」：记下当前重复策略，再切到编排。</summary>
    public static void SelectOrchestrated(IOrchestratedDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (IsOrchestrated(settings.DrawMode))
            return;

        settings.SampledDrawMode = NormalizeSampled(settings.DrawMode);
        settings.DrawMode = DrawMode.Orchestrated;
    }

    /// <summary>改回随机或公平抽取：把进入编排前的重复策略还回去。</summary>
    public static void LeaveOrchestrated(IOrchestratedDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsOrchestrated(settings.DrawMode))
            return;

        settings.DrawMode = NormalizeSampled(settings.SampledDrawMode);
    }

    /// <summary>
    ///     「抽取模式」下拉框当前应该显示的那一档重复策略：编排期间显示进入编排前记住的值，
    ///     否则就是正在生效的 <c>DrawMode</c>。
    /// </summary>
    /// <remarks>
    ///     这个值永远落在允许重复 / 不重复 / 半重复里，所以它适合绑下拉框的 `SelectedIndex`
    ///     （索引 0..2）。直接把 <c>DrawMode</c> 绑上去是不行的：编排抽取值是 3，下拉只有三项，
    ///     `SelectedIndex` 会被强制回 -1 再经 TwoWay 写回配置，既清空下拉又把编排状态悄悄丢掉。
    /// </remarks>
    public static DrawMode ResolveRepeatPolicy(IOrchestratedDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return IsOrchestrated(settings.DrawMode)
            ? NormalizeSampled(settings.SampledDrawMode)
            : NormalizeSampled(settings.DrawMode);
    }

    /// <summary>用户在「抽取模式」下拉框里换了一档：编排期间只记下来，不把通道从编排里拖出来。</summary>
    public static void SetRepeatPolicy(IOrchestratedDrawSettings settings, DrawMode repeatPolicy)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = NormalizeSampled(repeatPolicy);
        settings.SampledDrawMode = normalized;
        if (!IsOrchestrated(settings.DrawMode))
            settings.DrawMode = normalized;
    }

    /// <summary>
    ///     <c>DrawMode</c> 被外部改写（换抽取方式、导入配置、控制面下发）后，让记住的重复策略跟上它。
    ///     编排期间必须保持原样：那个值就是退出编排时要还回去的档位。
    /// </summary>
    public static void SynchronizeRepeatPolicy(IOrchestratedDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsOrchestrated(settings.DrawMode))
            settings.SampledDrawMode = NormalizeSampled(settings.DrawMode);
    }

    /// <summary>
    ///     恢复值只能是抽样策略。手改的配置文件、导入的旧存档或控制面下发都可能把它写成
    ///     <see cref="DrawMode.Orchestrated" />，那不是一个可恢复的重复策略，退回不重复。
    /// </summary>
    private static DrawMode NormalizeSampled(DrawMode mode) => mode switch
    {
        DrawMode.Repeat or DrawMode.NoRepeat or DrawMode.HalfRepeat => mode,
        _ => DrawMode.NoRepeat
    };
}