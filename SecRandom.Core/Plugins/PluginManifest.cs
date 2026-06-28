using System.Text.Json.Serialization;

namespace SecRandom.Core.Plugins;

public sealed class PluginManifest
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; init; } = string.Empty;
    [JsonPropertyName("author")] public string Author { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("apiVersion")] public string ApiVersion { get; init; } = string.Empty;
    [JsonPropertyName("minimumHostVersion")] public string MinimumHostVersion { get; init; } = string.Empty;
    [JsonPropertyName("entryAssembly")] public string EntryAssembly { get; init; } = string.Empty;
    [JsonPropertyName("entryType")] public string EntryType { get; init; } = string.Empty;
}
