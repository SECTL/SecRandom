namespace SecRandom.Core.Enums.Configs;

/// <summary>
///     How a fair student draw treats candidates that are already ahead of the class average.
/// </summary>
/// <remarks>
///     Both modes read the same per-student draw counts; they differ in whether the average gate is a hard
///     pool filter (<see cref="UniformFirst" />) or a zero-probability weight inside a full pool
///     (<see cref="RandomFirst" />).
/// </remarks>
public enum FairDrawMode
{
    /// <summary>
    ///     Hard average gate: only students at or below the average draw count stay in the candidate pool,
    ///     then the fair weights rank that already-narrowed pool.
    /// </summary>
    UniformFirst,

    /// <summary>
    ///     Soft average gate: every enabled student stays in the pool, students above the relaxed average
    ///     threshold get weight 0 and therefore can never be drawn, and the rest keep graded weights.
    /// </summary>
    RandomFirst
}