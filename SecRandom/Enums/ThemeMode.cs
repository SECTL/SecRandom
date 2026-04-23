using System.Text.Json.Serialization;

namespace SecRandom.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeMode
{
    [JsonStringEnumMemberName("LIGHT")]
    Light,
    
    [JsonStringEnumMemberName("DARK")]
    Dark,
    
    [JsonStringEnumMemberName("AUTO")]
    Auto
}