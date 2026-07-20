using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Services.Draw;

/// <summary>
///     Single source of truth for DrawMode → repeat-threshold semantics. Repeat returns 0 (no limit),
///     NoRepeat returns 1, HalfRepeat returns Max(1, configured value). All draw channels (pages, quick draw,
///     plugin invoker, sessions, engine internals) must resolve thresholds through this helper.
/// </summary>
public static class DrawRepeatPolicy
{
    public static int ResolveThreshold(DrawMode mode, int halfRepeat) => mode switch
    {
        DrawMode.Repeat => 0,
        DrawMode.NoRepeat => 1,
        DrawMode.HalfRepeat => Math.Max(1, halfRepeat),
        _ => 1
    };

    public static bool HasReachedLimit(int drawnCount, int threshold) => threshold > 0 && drawnCount >= threshold;
}
