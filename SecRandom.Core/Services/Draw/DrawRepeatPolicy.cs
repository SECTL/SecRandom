using SecRandom.Core.Enums.Configs;

namespace SecRandom.Core.Services.Draw;

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
