using System.Text.Json.Serialization;

namespace SecRandom.Core.Enums.Configs;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OmniTtsProvider
{
    [JsonStringEnumMemberName("OpenAI")] OpenAi,

    [JsonStringEnumMemberName("FishAudio")] FishAudio,

    [JsonStringEnumMemberName("MiMo")] MiMo,

    [JsonStringEnumMemberName("Gemini")] Gemini,

    [JsonStringEnumMemberName("Custom")] Custom
}
