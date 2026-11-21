namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     The one place that names a <see cref="DrawMode" /> for anything outside the app: the anonymous audit
///     payload submitted to <c>fair.sectl.cn</c> (<c>fairness.repeatMode</c>), the proof/verification page that
///     renders it, and the IPC/control-plane projection of the same setting.
/// </summary>
/// <remarks>
///     <para>
///         These strings are wire values, not display text: changing one invalidates every proof that already
///         carries it, and the verifier keeps its own fixed list that must stay byte-identical. They live here so
///         a token cannot be spelled two ways in two call sites, which is what previously made "which mode was
///         this draw" hard to answer across the app and the service.
///     </para>
///     <para>
///         <c>orchestrated</c> is not a repeat policy: the order was arranged before the draw. It therefore gets
///         its own token instead of pretending to be <c>no-repeat</c> — a reader of the proof has to be able to
///         tell that the draw was not sampled, and a verifier has to reject a repeat-limit reading of it.
///     </para>
/// </remarks>
public static class DrawModeCatalog
{
    public const string Repeat = "repeat";
    public const string NoRepeat = "no-repeat";
    public const string HalfRepeat = "half-repeat";
    public const string Orchestrated = "orchestrated";
    public const string Unknown = "unknown";

    /// <summary>
    ///     「抽取方式」下拉里代表编排抽取的那一项。它不是注册的抽样算法（没有权重函数可调），
    ///     只是各通道设置页把它和随机抽取、公平抽取摆在同一组时用的固定选项 id，
    ///     用户选中它时写的是 <see cref="DrawMode.Orchestrated" /> 而不是 <c>AlgorithmId</c>。
    /// </summary>
    public const string OrchestratedOptionId = "builtin.orchestrated";

    /// <summary>
    ///     Every token the audit protocol accepts, in declaration order. The verifier's allow-list mirrors this
    ///     list exactly; adding a fifth mode means adding it to both sides before a build emits it.
    /// </summary>
    public static IReadOnlyList<string> Tokens { get; } = [Repeat, NoRepeat, HalfRepeat, Orchestrated];

    /// <summary>The audit token for a mode; <c>unknown</c> for an undefined value, never a thrown exception.</summary>
    public static string ToToken(DrawMode mode) => mode switch
    {
        DrawMode.Repeat => Repeat,
        DrawMode.NoRepeat => NoRepeat,
        DrawMode.HalfRepeat => HalfRepeat,
        DrawMode.Orchestrated => Orchestrated,
        _ => Unknown
    };

    /// <summary>Reads a token back. Case-sensitive on purpose: the wire vocabulary is lowercase-only.</summary>
    public static bool TryParse(string? token, out DrawMode mode)
    {
        switch (token)
        {
            case Repeat:
                mode = DrawMode.Repeat;
                return true;
            case NoRepeat:
                mode = DrawMode.NoRepeat;
                return true;
            case HalfRepeat:
                mode = DrawMode.HalfRepeat;
                return true;
            case Orchestrated:
                mode = DrawMode.Orchestrated;
                return true;
            default:
                mode = default;
                return false;
        }
    }
}