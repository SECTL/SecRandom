namespace SecRandom.Platforms.Abstractions;

/// <summary>
///     屏幕物理像素矩形。采样只在屏幕坐标里工作，所以这里刻意不使用任何 Avalonia 类型。
/// </summary>
public readonly record struct PlatformPixelRect(int X, int Y, int Width, int Height)
{
    public static PlatformPixelRect Empty => new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>向外扩张 <paramref name="amount" /> 个像素（负数向内收缩）。</summary>
    public PlatformPixelRect Inflate(int amount) =>
        new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public PlatformPixelRect Intersect(PlatformPixelRect other)
    {
        var x = Math.Max(X, other.X);
        var y = Math.Max(Y, other.Y);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= x || bottom <= y ? Empty : new PlatformPixelRect(x, y, right - x, bottom - y);
    }
}

/// <summary>
///     一次背景采样的结果：线性相对亮度（0–1）的分位值，而不是颜色。
/// </summary>
/// <remarks>
///     只取亮度分位是有意的：HDR、广色域、远程桌面上像素颜色都不可靠，而"这片背景是深还是浅"
///     的结论只需要亮度。调用方据此决定前景色，不需要也不应该依赖具体色相。
/// </remarks>
public sealed record ScreenLuminanceSample(double P10, double Median, double P90, int PixelCount)
{
    public double Spread => Math.Max(0, P90 - P10);
}

/// <summary>
///     采样请求。<paramref name="Exclusions" /> 是必须先被剔除的矩形——通常是本应用自己的窗口，
///     否则已经显示的结果卡片会把采样结果变成它自己的底色。
/// </summary>
public sealed record ScreenLuminanceRequest(
    PlatformPixelRect Region,
    IReadOnlyList<PlatformPixelRect>? Exclusions = null);

/// <summary>
///     读取屏幕某块区域亮度分布的能力。桌面合成、锁屏、远程桌面等场景可能失败，失败一律返回
///     <see langword="null" />，调用方回退到应用主题，不得把失败当成"背景是黑的"。
/// </summary>
public interface IScreenLuminanceSampler
{
    /// <summary>当前平台是否真的实现了采样；未实现时 <see cref="Sample" /> 只会返回 null。</summary>
    bool IsSupported { get; }

    /// <summary>
    ///     同步采样（短时 CPU 密集，调用方负责放到线程池）。区域会先与虚拟屏求交，越界部分忽略。
    /// </summary>
    /// <returns>采样结果；区域为空、API 失败或安全桌面导致不可读时返回 <see langword="null" />。</returns>
    ScreenLuminanceSample? Sample(ScreenLuminanceRequest request);
}

/// <summary>没有屏幕采样能力的平台（Linux/macOS/移动端）使用的空实现。</summary>
public sealed class UnsupportedScreenLuminanceSampler : IScreenLuminanceSampler
{
    public static UnsupportedScreenLuminanceSampler Instance { get; } = new();

    private UnsupportedScreenLuminanceSampler()
    {
    }

    public bool IsSupported => false;

    public ScreenLuminanceSample? Sample(ScreenLuminanceRequest request) => null;
}