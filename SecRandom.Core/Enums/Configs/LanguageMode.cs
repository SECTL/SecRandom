using System.Text.Json.Serialization;

namespace SecRandom.Core.Enums.Configs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LanguageMode
{
    [JsonStringEnumMemberName("简体中文")]
    ChineseSimplified,
    
    [JsonStringEnumMemberName("English")]
    English,
    
    [JsonStringEnumMemberName("日本語")]
    Japanese,
}