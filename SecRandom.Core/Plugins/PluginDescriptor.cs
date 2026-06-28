namespace SecRandom.Core.Plugins;

public sealed class PluginDescriptor
{
    public required PluginManifest Manifest { get; init; }
    public required string DirectoryPath { get; init; }
    public PluginStatus Status { get; init; } = PluginStatus.Discovered;
    public bool IsEnabled { get; init; }
    public bool RequiresRestart { get; init; }
    public string? ErrorMessage { get; init; }

    public string Id => Manifest.Id;
    public string Name => string.IsNullOrWhiteSpace(Manifest.Name) ? Manifest.Id : Manifest.Name;
    public string Version => Manifest.Version;
    public string Author => Manifest.Author;
}
