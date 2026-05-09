using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Shared.Models;

public class AttachableSettingsObject : ObservableRecipient, IAttachableSettingsObject
{
    public Dictionary<Guid, object?> AttachedObjects { get; set; } = [];
}