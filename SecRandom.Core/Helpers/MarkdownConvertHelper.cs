using Markdown.Avalonia;

namespace SecRandom.Core.Helpers;

/// <summary>
/// Provides the shared Markdown engine used by plugin/settings rich-text rendering.
/// </summary>
public static class MarkdownConvertHelper
{
    private static Markdown.Avalonia.Markdown? _engine;

    /// <summary>
    /// Gets the shared Markdown engine.
    /// </summary>
    public static Markdown.Avalonia.Markdown Engine => _engine ??= new Markdown.Avalonia.Markdown();
}
