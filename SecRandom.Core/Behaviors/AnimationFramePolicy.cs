namespace SecRandom.Core.Behaviors;

/// <summary>
///     动画帧节奏策略：把「每帧间隔多少毫秒」从各处硬编码的 15/16ms 收敛成进程级一个值，
///     由性能档位（低配模式）在启动时与设置变更时写入。
///     <para>
///         逐帧动画的代价由帧数决定：一帧通常是「一次 <c>Dispatcher.UIThread.InvokeAsync</c> 往返
///         + 一次 <c>Task.Delay</c>」。60fps 的 16ms 意味着每秒 60 次跨线程往返，而且每次都在 UI 线程上
///         重写 <c>Opacity</c>/<c>RenderTransform</c> 触发重新合成。低配档把间隔放宽到 33ms（约 30fps）
///         可以把这部分开销直接减半，动画总时长不变、只是帧更少。
///     </para>
///     <para>
///         读取方必须在每帧循环开始前取一次值，不要在循环里反复读：档位切换在动画中途生效没有意义，
///         且会让同一个动画的帧间隔前后不一致。
///     </para>
/// </summary>
public static class AnimationFramePolicy
{
    /// <summary>标准档帧间隔（约 60fps），默认值</summary>
    public const int StandardFrameIntervalMs = 16;

    /// <summary>低配档帧间隔（约 30fps）</summary>
    public const int LowSpecFrameIntervalMs = 33;

    private static int _frameIntervalMs = StandardFrameIntervalMs;

    /// <summary>
    ///     当前帧间隔（毫秒）。写入值会被钳制在 8–200ms，避免误配成 0（忙等）或过大（动画不动）。
    /// </summary>
    public static int FrameIntervalMs
    {
        get => _frameIntervalMs;
        set => _frameIntervalMs = Math.Clamp(value, 8, 200);
    }

    /// <summary>按当前帧间隔把一个时长切成帧数（至少 1 帧）</summary>
    public static int ResolveFrameCount(int durationMs)
    {
        if (durationMs <= 0)
            return 1;

        return Math.Max(1, (int)Math.Round(durationMs / (double)FrameIntervalMs));
    }

    /// <summary>按当前帧间隔计算每帧应等待的毫秒数（至少 1ms）</summary>
    public static int ResolveFrameDelayMs(int durationMs, int frameCount)
    {
        if (durationMs <= 0 || frameCount <= 0)
            return FrameIntervalMs;

        return Math.Max(1, (int)Math.Round(durationMs / (double)frameCount));
    }

    /// <summary>性能档位切换时由宿主调用；<paramref name="lowSpec" /> 为真时降帧</summary>
    public static void ApplyLowSpecMode(bool lowSpec)
    {
        FrameIntervalMs = lowSpec ? LowSpecFrameIntervalMs : StandardFrameIntervalMs;
    }
}
