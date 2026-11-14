using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

/// <summary>
///     Windows 上的屏幕亮度采样：一次 GDI <c>BitBlt</c> 抓取目标区域，再在 CPU 上做一次亮度直方图。
/// </summary>
/// <remarks>
///     <para>
///         只抓目标矩形（典型 300×200），不是整屏：通知卡片要知道的只是它自己脚下那块桌面。
///         整屏 1920×1080 是 8.3MB 的拷贝，而目标区域是几百 KB，单次耗时在毫秒级。
///     </para>
///     <para>
///         这里刻意走 GDI 而不是 <c>Windows.Graphics.Capture</c>：后者的帧是异步投递的（16–33ms），
///         拿不到"窗口显示之前就已经算好"的时序；而且它需要 D3D11 环境，对一个只问黑白的小问题太重。
///     </para>
///     <para>
///         失败（安全桌面、远程会话、区域内没有任何像素）一律返回 <see langword="null" />。绝不能把
///         "抓不到"当成"背景是黑的"——那会让锁屏后的通知强行变成白字黑底。
///     </para>
/// </remarks>
public sealed class WindowsScreenLuminanceSampler : IScreenLuminanceSampler
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint Srccopy = 0x00CC0020;
    private const uint DibRgbColors = 0;
    private const int BitsPerPixel = 32;
    private const int HistogramSize = 1024;
    private const int MaxSamples = 65536;
    private const int MinimumSamples = 64;

    /// <summary>sRGB 字节到线性相对亮度的查找表：0.04045 以下走线性段，其余走 2.4 次幂。</summary>
    private static readonly double[] LinearLookup = BuildLinearLookup();

    public bool IsSupported => OperatingSystem.IsWindows();

    public ScreenLuminanceSample? Sample(ScreenLuminanceRequest request)
    {
        if (!IsSupported || request.Region.IsEmpty)
            return null;

        var virtualScreen = ReadVirtualScreen();
        var region = request.Region.Intersect(virtualScreen);
        if (region.IsEmpty)
            return null;

        var exclusions = request.Exclusions;
        if (exclusions is { Count: > 0 })
        {
            // 剔除矩形后可能什么都不剩（例如窗口正好盖住整个区域），这时直接判定为不可采样。
            if (IsFullyCovered(region, exclusions))
                return null;
        }

        var stride = ResolveStride(region);
        var histogram = new int[HistogramSize];
        var samples = 0;

        var screenDc = GetDC(nint.Zero);
        if (screenDc == nint.Zero)
            return null;

        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
                return null;

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = region.Width,
                // 负高度 = 自上而下的 DIB，省掉一次翻转的索引计算。
                Height = -region.Height,
                Planes = 1,
                BitCount = BitsPerPixel,
                Compression = 0,
                SizeImage = 0
            };

            bitmap = CreateDIBSection(memoryDc, ref header, DibRgbColors, out var bits, nint.Zero, 0);
            if (bitmap == nint.Zero || bits == nint.Zero)
                return null;

            var previous = SelectObject(memoryDc, bitmap);
            if (previous == nint.Zero)
                return null;

            try
            {
                if (!BitBlt(memoryDc, 0, 0, region.Width, region.Height, screenDc, region.X, region.Y, Srccopy))
                    return null;

                SamplePixels(bits, region, stride, exclusions, histogram, ref samples);
            }
            finally
            {
                SelectObject(memoryDc, previous);
            }
        }
        catch (Exception)
        {
            // GDI 在会话切换时可能抛 DllNotFound/EntryPoint 等异常，采样失败不该影响通知本身。
            return null;
        }
        finally
        {
            if (bitmap != nint.Zero)
                DeleteObject(bitmap);
            if (memoryDc != nint.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(nint.Zero, screenDc);
        }

        if (samples < MinimumSamples)
            return null;

        return new ScreenLuminanceSample(
            Percentile(histogram, samples, 0.10),
            Percentile(histogram, samples, 0.50),
            Percentile(histogram, samples, 0.90),
            samples);
    }

    private static unsafe void SamplePixels(
        nint bits,
        PlatformPixelRect region,
        int stride,
        IReadOnlyList<PlatformPixelRect>? exclusions,
        int[] histogram,
        ref int samples)
    {
        var rows = (byte*)bits;
        var rowBytes = region.Width * 4;
        for (var y = 0; y < region.Height; y += stride)
        {
            var row = rows + (long)y * rowBytes;
            var screenY = region.Y + y;
            for (var x = 0; x < region.Width; x += stride)
            {
                if (exclusions is { Count: > 0 } && IsExcluded(exclusions, region.X + x, screenY))
                    continue;

                var pixel = row + x * 4;
                var luminance = 0.2126 * LinearLookup[pixel[2]]
                                + 0.7152 * LinearLookup[pixel[1]]
                                + 0.0722 * LinearLookup[pixel[0]];
                var bucket = (int)(luminance * HistogramSize);
                if (bucket >= HistogramSize)
                    bucket = HistogramSize - 1;
                else if (bucket < 0)
                    bucket = 0;
                histogram[bucket]++;
                samples++;
            }
        }
    }

    private static bool IsExcluded(IReadOnlyList<PlatformPixelRect> exclusions, int x, int y)
    {
        for (var i = 0; i < exclusions.Count; i++)
        {
            if (exclusions[i].Contains(x, y))
                return true;
        }

        return false;
    }

    /// <summary>剔除矩形是否已经把整个区域盖满。</summary>
    private static bool IsFullyCovered(PlatformPixelRect region, IReadOnlyList<PlatformPixelRect> exclusions)
    {
        // 矩形数量极少（本应用自己的窗口），逐像素判定成本高于直接做覆盖格点检查，但区域很小，
        // 这里用"网格抽样"近似：任何未被覆盖的格点存在就认为仍有可采样的像素。
        const int grid = 16;
        for (var row = 0; row < grid; row++)
        {
            for (var column = 0; column < grid; column++)
            {
                var x = region.X + region.Width * column / grid;
                var y = region.Y + region.Height * row / grid;
                if (!IsExcluded(exclusions, x, y))
                    return false;
            }
        }

        return true;
    }

    private static int ResolveStride(PlatformPixelRect region)
    {
        var pixels = (long)region.Width * region.Height;
        if (pixels <= MaxSamples)
            return 1;

        var stride = (int)Math.Ceiling(Math.Sqrt(pixels / (double)MaxSamples));
        return Math.Max(1, stride);
    }

    private static PlatformPixelRect ReadVirtualScreen()
    {
        return new PlatformPixelRect(
            GetSystemMetrics(SmXVirtualScreen),
            GetSystemMetrics(SmYVirtualScreen),
            GetSystemMetrics(SmCxVirtualScreen),
            GetSystemMetrics(SmCyVirtualScreen));
    }

    private static double Percentile(int[] histogram, int total, double percentile)
    {
        var target = (int)Math.Ceiling(total * percentile);
        if (target < 1)
            target = 1;

        var cumulative = 0;
        for (var index = 0; index < histogram.Length; index++)
        {
            cumulative += histogram[index];
            if (cumulative >= target)
                return (index + 0.5) / histogram.Length;
        }

        return 1;
    }

    private static double[] BuildLinearLookup()
    {
        var lookup = new double[256];
        for (var i = 0; i < lookup.Length; i++)
        {
            var value = i / 255.0;
            lookup[i] = value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return lookup;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDc);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint hdc,
        ref BitmapInfoHeader pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        nint hdcDest,
        int x,
        int y,
        int cx,
        int cy,
        nint hdcSrc,
        int x1,
        int y1,
        uint rop);
}