using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SecRandom.Shared.Models;

public class AttachableSettingsObject : ObservableRecipient
{
    public Dictionary<Guid, object?> AttachedObjects { get; set; } = [];
    
    public T? GetAttachedObject<T>(Guid id)
    {
        AttachedObjects.TryGetValue(id, out var o);
        if (o is JsonElement o1)
        {
            return o1.Deserialize<T>();
        }
        return (T?)o;
    }
    
    public void WriteAttachedObject<T>(Guid id, T o)
    {
        AttachedObjects[id] = o;
    }
    
    public T GetAttachedObject<T>(Guid id, T defaultValue)
    {
        var r = GetAttachedObject<T>(id);
        if (r != null)
        {
            WriteAttachedObject(id, r);
            return r;
        }
        WriteAttachedObject(id, defaultValue);
        return defaultValue;
    }
}