namespace SecRandom.Platforms.Abstractions;

/// <summary>应用图标的像素布局。</summary>
public enum PlatformIconPixelFormat
{
    /// <summary>已经是 PNG 文件字节。</summary>
    Png = 0,

    /// <summary>未压缩的 BGRA32 像素，行优先、不带行填充。</summary>
    Bgra32 = 1
}

/// <summary>平台提取到的一张应用图标。</summary>
public sealed record PlatformIconImage(
    int Width,
    int Height,
    byte[] Pixels,
    PlatformIconPixelFormat Format)
{
    /// <summary>
    ///     PNG 格式只需要字节，尺寸由应用层解码时确定；BGRA32 则必须给出自洽的尺寸。
    /// </summary>
    public bool IsValid => Format switch
    {
        PlatformIconPixelFormat.Png => Pixels is { Length: > 8 },
        PlatformIconPixelFormat.Bgra32 =>
            Width > 0 && Height > 0 && Pixels is { Length: > 0 } && Pixels.Length >= Width * Height * 4,
        _ => false
    };
}

/// <summary>应用图标提取失败的原因。由应用层翻译成用户可读的提示。</summary>
public enum PlatformIconExtractionError
{
    None = 0,

    /// <summary>选中的文件不是该平台的应用/可执行文件。</summary>
    NotAnApplication,

    /// <summary>是应用，但里面找不到可用的图标资源。</summary>
    NoIconFound,

    /// <summary>图标存在，但格式（如 Linux 的 SVG/XPM）无法解码。</summary>
    UnsupportedFormat,

    /// <summary>读取或平台调用失败。</summary>
    Failed
}

/// <summary>一次应用图标提取的结果。</summary>
public sealed record PlatformIconExtractionResult(
    PlatformIconImage? Image,
    PlatformIconExtractionError Error)
{
    public bool IsSuccess => Image is { IsValid: true } && Error == PlatformIconExtractionError.None;

    public static PlatformIconExtractionResult Success(PlatformIconImage image)
    {
        return new PlatformIconExtractionResult(image, PlatformIconExtractionError.None);
    }

    public static PlatformIconExtractionResult Failure(PlatformIconExtractionError error)
    {
        return new PlatformIconExtractionResult(null, error);
    }
}

/// <summary>
///     从平台的应用/可执行文件中读取它对外展示的图标。
/// </summary>
/// <remarks>
///     实现必须是只读、无副作用的，并自行限制读取大小与耗时；调用方可能拿任意用户选择的路径来调用。
/// </remarks>
public interface IPlatformApplicationIconExtractor
{
    /// <summary>
    ///     提取图标。<paramref name="path" /> 可以是可执行文件、快捷方式、应用包目录或图标文件本身，
    ///     具体接受范围由平台决定。
    /// </summary>
    PlatformIconExtractionResult Extract(string path);
}
