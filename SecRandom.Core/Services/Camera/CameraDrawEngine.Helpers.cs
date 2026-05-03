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
                Source: desc.Identity?.ToString() ?? string.Empty,
                Resolutions: desc.Characteristics.Select<VideoCharacteristics, CameraResolutionInfo>(characteristics =>
                    new CameraResolutionInfo(Width: characteristics.Width, Height: characteristics.Height,
                        PixelFormat: characteristics.RawPixelFormat, Fps: characteristics.FramesPerSecond)).ToList()
            );
            list.Add(dev);
        }

        return list;
    }

    private static CaptureDeviceDescriptor? getFirstDescriptorByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var devices = new CaptureDevices();
        var descriptor = devices
            .EnumerateDescriptors()
            .FirstOrDefault(desc => desc.Identity?.ToString() == name);

        return descriptor;
    }

    private static (int Width, int Height) parseResolution(string resolutionString)
    {
        if (string.IsNullOrWhiteSpace(resolutionString))
            return (0, 0);

        string[] parts = resolutionString.Split('x');
        if (parts.Length != 2)
            return (0, 0);

        if (!int.TryParse(parts[0].Trim(), out int width) ||
            !int.TryParse(parts[1].Trim(), out int height))
            return (0, 0);

        return (width, height);
    }
}
