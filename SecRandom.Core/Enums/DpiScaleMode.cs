using System.Text.Json.Serialization;

namespace SecRandom.Core.Enums;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DpiScaleMode
{
    [JsonStringEnumMemberName("100%")]
    D100,
    
    [JsonStringEnumMemberName("125%")]
    D125,
    
    [JsonStringEnumMemberName("150%")]
    D150,
    
    [JsonStringEnumMemberName("175%")]
    D175,
    
    [JsonStringEnumMemberName("200%")]
    D200,
    
    [JsonStringEnumMemberName("Auto")]
    Auto
}