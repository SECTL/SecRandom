namespace SecRandom.Platforms.Abstractions;

/// <summary>没有原生图标提取实现的平台使用的占位实现。</summary>
public sealed class UnsupportedApplicationIconExtractor : IPlatformApplicationIconExtractor
{
    public static UnsupportedApplicationIconExtractor Instance { get; } = new();

    private UnsupportedApplicationIconExtractor()
    {
    }

    public PlatformIconExtractionResult Extract(string path)
    {
        return PlatformIconExtractionResult.Failure(PlatformIconExtractionError.NotAnApplication);
    }
}
