using System.Text.Encodings.Web;
using System.Text.Json;
using SecRandom.Core.Converters;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Abstraction;

public abstract class ConfigServiceBase
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new ColorJsonConverter() },
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public abstract bool IsConfigExists<T>(T fallback) where T : ConfigBase;
    public abstract T LoadConfig<T>(T fallback) where T : ConfigBase;
    public abstract void SaveConfig<T>(T config) where T : ConfigBase;
    public abstract void DeleteConfig<T>(T config) where T : ConfigBase;
}