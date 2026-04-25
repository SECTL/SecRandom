using System.Text.Json.Serialization;

namespace SecRandom.Core.Abstraction;

public abstract class ProfileConfigBase : ConfigBase
{
    [JsonIgnore] public abstract string Name { get; set; }
}