using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Core.Services.Notification;

/// <summary>A roster-owned reference that survives roster renames and member imports.</summary>
public sealed class TextLibraryBinding
{
    private static readonly Guid BindingId = new("6f92228b-67a4-4d46-9c80-974dcd617fea");
    public string? LibraryId { get; set; }

    public static string? Get(IAttachableSettingsObject? roster) =>
        roster?.GetAttachedObject<TextLibraryBinding>(BindingId)?.LibraryId;

    public static void Set(IAttachableSettingsObject roster, string? libraryId)
    {
        if (string.IsNullOrEmpty(libraryId)) roster.AttachedObjects.Remove(BindingId);
        else roster.AttachedObjects[BindingId] = new TextLibraryBinding { LibraryId = libraryId };
    }
}
