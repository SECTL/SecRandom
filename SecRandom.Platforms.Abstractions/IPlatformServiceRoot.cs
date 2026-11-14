namespace SecRandom.Platforms.Abstractions;

public interface IPlatformServiceRoot
{
    PlatformKind Kind { get; }

    PlatformCapabilities Capabilities { get; }

    IWindowFeatureService WindowFeatures { get; }

    IRemovableStorageCatalog RemovableStorage { get; }

    IRemovableStorageBindingMarker RemovableStorageBindingMarker { get; }

    IPlatformCameraDeviceCatalog CameraDevices { get; }

    /// <summary>屏幕亮度采样能力；只有真正实现了截屏的平台才会声明支持。</summary>
    IScreenLuminanceSampler ScreenLuminance { get; }

    /// <summary>从本机应用文件读取图标的能力。</summary>
    IPlatformApplicationIconExtractor ApplicationIcons { get; }
}
