using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Shared.Extensions;
using SecRandom.Shared.Interfaces;

namespace SecRandom.Shared.Models;

public class AttachableSettingsObject : ObservableRecipient, IAttachableSettingsObject
{
    public Dictionary<Guid, object?> AttachedObjects { get; set; } = [];
}