using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Services.FloatingWindow;
using SecRandom.Platforms.Abstractions;
using SecRandom.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SecRandom.Services.FloatingWindow;

/// <summary>自定义按钮图标导入失败的原因。</summary>
public enum FloatingWindowIconImportError
{
    None = 0,
    Unreadable,
    TooLarge,
    UnsupportedFormat,
    NotAnApplication,
    NoIconFound,
    ExtractionFailed
}

/// <summary>一次图标导入的结果；成功时 <see cref="FileName" /> 为配置里应保存的文件名。</summary>
public sealed record FloatingWindowIconImportResult(string? FileName, FloatingWindowIconImportError Error)
{
    public bool IsSuccess => Error == FloatingWindowIconImportError.None && !string.IsNullOrWhiteSpace(FileName);
}

/// <summary>
///     管理悬浮窗自定义按钮的图标文件：导入图片、从应用程序提取图标、读取预览、清理孤儿文件。
/// </summary>
/// <remarks>
///     图标统一转成不超过 256px 的 PNG，存放在 <c>data/images/floating-window-icons</c>
///     （该目录已在全量/云备份根里）。配置只保存文件名，读取时再做路径校验。
/// </remarks>
public sealed class FloatingWindowIconService
{
    public const string IconFolderName = "floating-window-icons";
    private const int MaxImportBytes = 8 * 1024 * 1024;
    private const int MaxSourcePixels = 4096 * 4096;
    private const int MaxStoredDimension = 256;

    private readonly ConcurrentDictionary<string, Bitmap?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IPlatformApplicationIconExtractor _platformExtractor;
    private readonly ILogger<FloatingWindowIconService> _logger;

    public FloatingWindowIconService(
        IPlatformApplicationIconExtractor platformExtractor,
        ILogger<FloatingWindowIconService> logger)
    {
        _platformExtractor = platformExtractor;
        _logger = logger;
    }

    public static string DirectoryPath => Utils.GetDirectoryPath("images", IconFolderName);

    /// <summary>读取（并缓存）图标预览；文件缺失或损坏时返回 <c>null</c>。</summary>
    public Bitmap? TryLoadBitmap(string? fileName)
    {
        if (!FloatingWindowButtonCatalog.IsSafeImageName(fileName))
            return null;

        return _bitmapCache.GetOrAdd(fileName!, static (name, service) => service.LoadBitmap(name), this);
    }

    /// <summary>
    ///     导入用户选择的图片文件。图片会被解码、必要时缩放到 256px 并重新编码为 PNG。
    /// </summary>
    public async Task<FloatingWindowIconImportResult> ImportImageAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBoundedAsync(stream, cancellationToken);
        if (bytes is null)
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.TooLarge);
        if (bytes.Length == 0)
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.Unreadable);

        try
        {
            var info = Image.Identify(bytes);
            if (info is null)
                return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.UnsupportedFormat);
            if ((long)info.Width * info.Height > MaxSourcePixels)
                return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.TooLarge);

            using var image = Image.Load(bytes);
            return await SaveNormalizedAsync(image, cancellationToken);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.UnsupportedFormat);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to import a floating window button icon from an image.");
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.Unreadable);
        }
    }

    /// <summary>
    ///     从应用/可执行文件提取图标并保存为托管 PNG。
    /// </summary>
    public async Task<FloatingWindowIconImportResult> ImportApplicationIconAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        PlatformIconExtractionResult extracted;
        try
        {
            extracted = _platformExtractor.Extract(path);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Platform icon extraction failed for {Path}.", path);
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.ExtractionFailed);
        }

        if (!extracted.IsSuccess || extracted.Image is null)
            return new FloatingWindowIconImportResult(null, MapError(extracted.Error));

        try
        {
            var icon = extracted.Image;
            Image image = icon.Format switch
            {
                PlatformIconPixelFormat.Png => Image.Load(icon.Pixels),
                PlatformIconPixelFormat.Bgra32 => Image.LoadPixelData<Bgra32>(icon.Pixels, icon.Width, icon.Height),
                _ => throw new NotSupportedException("Unknown icon pixel format.")
            };

            using (image)
            {
                return await SaveNormalizedAsync(image, cancellationToken);
            }
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.UnsupportedFormat);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to store an extracted application icon from {Path}.", path);
            return new FloatingWindowIconImportResult(null, FloatingWindowIconImportError.ExtractionFailed);
        }
    }

    /// <summary>删除单个托管图标；被多行引用时由调用方判断是否仍在使用。</summary>
    public bool TryDelete(string? fileName)
    {
        if (!FloatingWindowButtonCatalog.IsSafeImageName(fileName))
            return false;

        _bitmapCache.TryRemove(fileName!, out _);
        try
        {
            var path = Path.Combine(DirectoryPath, fileName!);
            if (!File.Exists(path))
                return true;

            File.Delete(path);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Failed to delete floating window icon {FileName}.", fileName);
            return false;
        }
    }

    /// <summary>
    ///     清理不再被任何自定义按钮引用的图标文件。页面加载时调用一次即可。
    /// </summary>
    public void Prune(IEnumerable<string?> keepFileNames)
    {
        var keep = keepFileNames
            .Where(FloatingWindowButtonCatalog.IsSafeImageName)
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string[] files;
        try
        {
            if (!Directory.Exists(DirectoryPath))
                return;
            files = Directory.GetFiles(DirectoryPath, "*.png", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(e, "Failed to enumerate floating window icons for cleanup.");
            return;
        }

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (keep.Contains(name))
                continue;
            TryDelete(name);
        }
    }

    private Bitmap? LoadBitmap(string fileName)
    {
        try
        {
            var path = Path.Combine(DirectoryPath, fileName);
            if (!File.Exists(path))
                return null;

            return new Bitmap(path);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to load floating window icon {FileName}.", fileName);
            return null;
        }
    }

    private async Task<FloatingWindowIconImportResult> SaveNormalizedAsync(
        Image image,
        CancellationToken cancellationToken)
    {
        Image? frame = null;
        try
        {
            if (image.Frames.Count > 1)
            {
                frame = image.Frames.CloneFrame(0);
                image = frame;
            }

            if (image.Width > MaxStoredDimension || image.Height > MaxStoredDimension)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(MaxStoredDimension, MaxStoredDimension),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            var directory = DirectoryPath;
            Directory.CreateDirectory(directory);
            var fileName = $"{Guid.NewGuid():N}.png";
            var targetPath = Path.Combine(directory, fileName);
            var temporaryPath = targetPath + ".tmp";
            try
            {
                await image.SaveAsPngAsync(temporaryPath, cancellationToken);
                File.Move(temporaryPath, targetPath, true);
            }
            catch
            {
                TryDeleteFile(temporaryPath);
                throw;
            }

            return new FloatingWindowIconImportResult(fileName, FloatingWindowIconImportError.None);
        }
        finally
        {
            frame?.Dispose();
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read <= 0)
                break;
            if (buffer.Length + read > MaxImportBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception)
        {
            // 临时文件清理失败不影响导入结果。
        }
    }

    private static FloatingWindowIconImportError MapError(PlatformIconExtractionError error)
    {
        return error switch
        {
            PlatformIconExtractionError.NotAnApplication => FloatingWindowIconImportError.NotAnApplication,
            PlatformIconExtractionError.NoIconFound => FloatingWindowIconImportError.NoIconFound,
            PlatformIconExtractionError.UnsupportedFormat => FloatingWindowIconImportError.UnsupportedFormat,
            PlatformIconExtractionError.Failed => FloatingWindowIconImportError.ExtractionFailed,
            _ => FloatingWindowIconImportError.ExtractionFailed
        };
    }
}
