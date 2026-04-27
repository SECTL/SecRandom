using System.Text.Json.Serialization;

namespace SecRandom.Shared.Abstraction;

public abstract class ProfileConfigBase : ConfigBase
{
    [JsonIgnore] public abstract string Name { get; set; }
}