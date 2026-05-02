namespace SecRandom.Shared.Interfaces;

public interface IAttachableSettingsObject
{
    public Dictionary<Guid, object?> AttachedObjects { get; set; }
}