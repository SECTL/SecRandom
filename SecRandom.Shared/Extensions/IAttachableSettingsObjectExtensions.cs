using System.Text.Json;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Shared.Extensions;

public static class AttachableSettingsObjectExtensions
{
    public static T? GetAttachedObject<T>(this IAttachableSettingsObject obj, Guid id)
    {
        obj.AttachedObjects.TryGetValue(id, out var o);
        if (o is JsonElement o1) return o1.Deserialize<T>();

        return (T?)o;
    }

    public static void WriteAttachedObject<T>(this IAttachableSettingsObject obj, Guid id, T o)
    {
        obj.AttachedObjects[id] = o;
    }

    public static T GetAttachedObject<T>(this IAttachableSettingsObject obj, Guid id, T defaultValue)
    {
        var r = obj.GetAttachedObject<T>(id);
        if (r != null)
        {
            obj.WriteAttachedObject(id, r);
            return r;
        }

        obj.WriteAttachedObject(id, defaultValue);
        return defaultValue;
    }
}