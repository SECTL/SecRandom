namespace SecRandom.PluginSdk;

public sealed class PluginInfo
{
    public required PluginManifest Manifest { get; init; }
    public required string PluginFolderPath { get; init; }
    public required string PluginConfigFolder { get; init; }
    public PluginLoadStatus LoadStatus { get; internal set; } = PluginLoadStatus.NotLoaded;
    public Exception? Exception { get; internal set; }
    public bool IsEnabled { get; internal set; } = true;
}
