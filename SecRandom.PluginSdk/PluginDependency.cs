namespace SecRandom.PluginSdk;

public sealed class PluginDependency
{
    public string Id { get; set; } = string.Empty;
    public bool Required { get; set; } = true;
}
