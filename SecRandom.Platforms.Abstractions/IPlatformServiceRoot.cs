namespace SecRandom.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }

    IRemovableStorageCatalog RemovableStorage { get; }

    IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; }
}
