namespace SecRandom.Core.Models.Camera;

public sealed record CameraDeviceInfo(
    string Name,
    string Source,
    IReadOnlyList<CameraResolutionInfo> Resolutions);

public sealed record CameraResolutionInfo(
    int Width,
    int Height,
    string PixelFormat,
    double Fps);