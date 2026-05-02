using FlashCap;
using SecRandom.Core.Models.Camera;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    public static List<CameraDeviceInfo> GetAllCameraDevices()
    {
        List<CameraDeviceInfo> list = [];
        var devices = new CaptureDevices();
        foreach (var desc in devices.GetDescriptors())
        {
            CameraDeviceInfo dev = new CameraDeviceInfo
            (
                Name: desc.Name,
                Source: desc.Identity.ToString(),
                Resolutions: desc.Characteristics.Select<VideoCharacteristics, CameraResolutionInfo>(characteristics =>
                    new CameraResolutionInfo(Width: characteristics.Width, Height: characteristics.Height,
                        PixelFormat: characteristics.RawPixelFormat, Fps: characteristics.FramesPerSecond)).ToList()
            );
            list.Add(dev);
        }

        return list;
    }

    private static VideoCharacteristics? getFirstCharacteristicsByIdentifier(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return null;

        var devices = new CaptureDevices();
        var descriptor = devices
            .EnumerateDescriptors()
            .FirstOrDefault(desc => desc.Identity?.ToString() == identifier);

        return descriptor?.Characteristics.FirstOrDefault();
    }
}
