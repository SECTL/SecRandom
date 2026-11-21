namespace SecRandom.Core.Enums.Configs;

public enum DrawMode
{
    Repeat,
    NoRepeat,
    HalfRepeat,

    /// <summary>
    ///     The order is arranged before the draw instead of being sampled at draw time. It is a mode of its
    ///     own rather than a fourth repeat policy: repeat limits, average-gap protection and fair weights are
    ///     all bypassed because the participant is already decided.
    /// </summary>
    /// <remarks>
    ///     The settings pages never list this value among the repeat policies — 编排抽取 sits in the
    ///     「抽取方式」 dropdown next to the random and fair algorithms, because it answers "who is drawn"
    ///     rather than "how are repeated draws handled". Selecting it stores this value (the draw side reads
    ///     only <c>DrawMode</c>) after saving the previous repeat policy in
    ///     <c>OverridableDrawSettings.SampledDrawMode</c>; see <c>DrawModeSelection</c>.
    /// </remarks>
    Orchestrated
}