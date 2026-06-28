using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecRandom.Shared.Converters;

public class LenientGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            return Guid.Empty;

        var value = reader.GetString();
        return Guid.TryParse(value, out var guid) ? guid : Guid.Empty;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
