using Avalonia.Controls;
using SecRandom.Core.Enums;

namespace SecRandom.Core.Plugins;

public sealed class PluginPageRegistration
{
    public required string PluginId { get; init; }
    public required string PageId { get; init; }
    public required string Name { get; init; }
    public required string IconGlyph { get; init; }
    public required Type PageType { get; init; }
    public string? GroupId { get; init; }
    public PageLocation Location { get; init; } = PageLocation.Top;
    public bool IsHide { get; init; }
    public bool UseFullWidth { get; init; }
    public bool HidePageTitle { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PluginId))
            throw new ArgumentException("Plugin id is required.", nameof(PluginId));

        if (!PageId.StartsWith($"plugin.{PluginId}.", StringComparison.Ordinal))
            throw new ArgumentException($"Plugin page id must start with plugin.{PluginId}.", nameof(PageId));

        if (!typeof(UserControl).IsAssignableFrom(PageType))
            throw new ArgumentException("Plugin page type must inherit Avalonia.Controls.UserControl.", nameof(PageType));

        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Plugin page name is required.", nameof(Name));
    }
}
