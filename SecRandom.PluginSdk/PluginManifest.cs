namespace SecRandom.PluginSdk;

public sealed class PluginManifest
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ApiVersion { get; set; } = string.Empty;
    public string EntranceAssembly { get; set; } = string.Empty;
    public string Icon { get; set; } = "icon.png";
    public string Readme { get; set; } = "README.md";
    public string? Url { get; set; }
    public string Author { get; set; } = string.Empty;
    public List<PluginDependency> Dependencies { get; set; } = [];
    public List<string> SupportedPlatforms { get; set; } = [];
}
