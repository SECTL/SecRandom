namespace SecRandom.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }

    IRemovableStorageCatalog RemovableStorage { get; }

    IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; }

    IPlatformCameraDeviceCatalog CameraDevices { get; }

    /// <summary>从本机应用文件读取图标的能力。</summary>
    IPlatformApplicationIconExtractor ApplicationIcons { get; }
}
