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
            var dev = new CameraDeviceInfo
            (
                desc.Name,
                desc.Identity?.ToString() ?? string.Empty,
                desc.Characteristics.Select<VideoCharacteristics, CameraResolutionInfo>(characteristics =>
                    new CameraResolutionInfo(characteristics.Width, characteristics.Height,
                        characteristics.RawPixelFormat, characteristics.FramesPerSecond)).ToList()
            );
            list.Add(dev);
        }

        return list;
    }

    private static CaptureDeviceDescriptor? GetFirstDescriptorByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        var devices = new CaptureDevices();
        var descriptor = devices
            .EnumerateDescriptors()
            .FirstOrDefault(desc => desc.Identity?.ToString() == name);

        return descriptor;
    }

    private static (int Width, int Height) ParseResolution(string resolutionString)
    {
        if (string.IsNullOrWhiteSpace(resolutionString))
            return (0, 0);

        var parts = resolutionString.Split('x');
        if (parts.Length != 2)
            return (0, 0);

        if (!int.TryParse(parts[0].Trim(), out var width) ||
            !int.TryParse(parts[1].Trim(), out var height))
            return (0, 0);

        return (width, height);
    }
}