using System.Text.Json;

namespace SecRandom.Shared.Interfaces;

public interface IAttachedSettings
{
    public bool IsAttachSettingsEnabled { get; set; }
    
    public static bool GetIsEnabled(object? obj)
    {
        if (obj == null)
        {
            return false;
        }
        return obj switch
        {
            JsonElement json when json.TryGetProperty(nameof(IsAttachSettingsEnabled), out var element) =>
                element.ValueKind == JsonValueKind.True,
            IAttachedSettings settings => settings.IsAttachSettingsEnabled,
            _ => false
        };
    }
}