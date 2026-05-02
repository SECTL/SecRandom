using System.Text.Json.Serialization;

namespace SecRandom.Core.Enums.Configs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TelemetryMode
{
    [JsonStringEnumMemberName("full")]
    Full,

    [JsonStringEnumMemberName("anonymous")]
    Anonymous,

    [JsonStringEnumMemberName("off")]
    Off,
}