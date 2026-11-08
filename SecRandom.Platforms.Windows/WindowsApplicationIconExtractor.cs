using System.Runtime.InteropServices;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

/// <summary>
///     通过 Windows Shell 读取 <c>.exe</c> / <c>.lnk</c> / <c>.ico</c> 文件对外展示的图标。
/// </summary>
/// <remarks>
///     优先用 <c>SHDefExtractIcon</c> 直接要 256px 大图，失败再退回 <c>SHGetFileInfo</c> 的 32px 图标。
///     句柄在用完后全部释放；图标转换成 BGRA32，交给应用层统一编码与缩放。
/// </remarks>
public sealed class WindowsApplicationIconExtractor : IPlatformApplicationIconExtractor
{
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiLargeIcon = 0x00000000;
    private const uint DibRgbColors = 0;
    private const int MaxIconDimension = 1024;

    public PlatformIconExtractionResult Extract(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.Failed);

        var extension = Path.GetExtension(path);
        if (!IsSupportedApplicationFile(extension))
            return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NotAnApplication);

        if (TryExtractDefIcon(path, out var width, out var height, out var pixels) ||
            TryExtractShellIcon(path, out width, out height, out pixels))
        {
            return PlatformIconExtractionResult.Success(
                new PlatformIconImage(width, height, pixels, PlatformIconPixelFormat.Bgra32));
        }

        return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NoIconFound);
    }

    private static bool IsSupportedApplicationFile(string extension)
    {
        return extension.ToLowerInvariant() is ".exe" or ".lnk" or ".ico" or ".dll";
    }

    private static bool TryExtractDefIcon(string path, out int width, out int height, out byte[] pixels)
    {
        width = height = 0;
        pixels = [];

        // 低 16 位是大图标尺寸，高 16 位是小图标尺寸。
        var size = 256u | (16u << 16);
        if (SHDefExtractIcon(path, 0, 0, out var large, out var small, size) != 0 || large == IntPtr.Zero)
        {
            if (small != IntPtr.Zero)
                DestroyIcon(small);
            return false;
        }

        try
        {
            return TryConvertIcon(large, out width, out height, out pixels);
        }
        finally
        {
            DestroyIcon(large);
            if (small != IntPtr.Zero)
                DestroyIcon(small);
        }
    }

    private static bool TryExtractShellIcon(string path, out int width, out int height, out byte[] pixels)
    {
        width = height = 0;
        pixels = [];

        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            path,
            0,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiLargeIcon);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            return false;

        try
        {
            return TryConvertIcon(info.hIcon, out width, out height, out pixels);
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static bool TryConvertIcon(IntPtr icon, out int width, out int height, out byte[] pixels)
    {
        width = height = 0;
        pixels = [];

        if (!GetIconInfo(icon, out var iconInfo))
            return false;

        try
        {
            if (iconInfo.hbmColor == IntPtr.Zero)
                return false;

            var bitmap = new Bitmap();
            if (GetObject(iconInfo.hbmColor, Marshal.SizeOf<Bitmap>(), ref bitmap) == 0)
                return false;

            width = bitmap.bmWidth;
            height = Math.Abs(bitmap.bmHeight);
            if (width <= 0 || height <= 0 || width > MaxIconDimension || height > MaxIconDimension)
                return false;

            var color = new byte[width * height * 4];
            var header = new BitmapInfo
            {
                bmiHeader = new BitmapInfoHeader
                {
                    biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                }
            };

            var dc = CreateCompatibleDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return false;

            try
            {
                if (GetDIBits(dc, iconInfo.hbmColor, 0, (uint)height, color, ref header, DibRgbColors) == 0)
                    return false;

                var hasAlpha = false;
                for (var i = 3; i < color.Length; i += 4)
                {
                    if (color[i] == 0)
                        continue;
                    hasAlpha = true;
                    break;
                }

                if (!hasAlpha && !TryApplyMaskAlpha(dc, iconInfo.hbmMask, width, height, color))
                    FillOpaqueAlpha(color);

                pixels = color;
                return true;
            }
            finally
            {
                DeleteDC(dc);
            }
        }
        finally
        {
            if (iconInfo.hbmColor != IntPtr.Zero)
                DeleteObject(iconInfo.hbmColor);
            if (iconInfo.hbmMask != IntPtr.Zero)
                DeleteObject(iconInfo.hbmMask);
        }
    }

    private static bool TryApplyMaskAlpha(nint dc, nint mask, int width, int height, byte[] color)
    {
        if (mask == IntPtr.Zero)
            return false;

        var bytesPerRow = (width + 31) / 32 * 4;
        var maskBits = new byte[bytesPerRow * height];
        var header = new BitmapInfo
        {
            bmiHeader = new BitmapInfoHeader
            {
                biSize = Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 1,
                biCompression = 0
            }
        };

        if (GetDIBits(dc, mask, 0, (uint)height, maskBits, ref header, DibRgbColors) == 0)
            return false;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var opaque = (maskBits[y * bytesPerRow + x / 8] & (0x80 >> (x % 8))) == 0;
                color[(y * width + x) * 4 + 3] = opaque ? (byte)255 : (byte)0;
            }
        }

        return true;
    }

    private static void FillOpaqueAlpha(byte[] color)
    {
        for (var i = 3; i < color.Length; i += 4)
            color[i] = 255;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHDefExtractIcon(
        string iconFile,
        int iconIndex,
        uint flags,
        out IntPtr largeIcon,
        out IntPtr smallIcon,
        uint iconSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetIconInfo(IntPtr icon, out IconInfo iconInfo);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr handle, int size, ref Bitmap bitmap);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(
        IntPtr dc,
        IntPtr bitmap,
        uint startScan,
        uint scanLines,
        byte[] bits,
        ref BitmapInfo info,
        uint usage);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader bmiHeader;

        // 32bpp/1bpp 且不设调色板时，GetDIBits 不读取颜色表。
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] bmiColors;
    }
}
