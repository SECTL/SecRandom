using System.Text.Json;

namespace SecRandom.Core.Abstraction;

public abstract class ConfigServiceBase
{
    protected readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new ColorJsonConverter() }
    };

    public abstract bool IsConfigExists<T>(T fallback) where T : ConfigBase;
    public abstract T LoadConfig<T>(T fallback) where T : ConfigBase;
    public abstract void SaveConfig<T>(T config) where T : ConfigBase;
    public abstract void DeleteConfig<T>(T config) where T : ConfigBase;
}