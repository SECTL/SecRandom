using Android.Content;
using Android.Hardware.Camera2;
using Java.Lang;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Android;

public sealed class AndroidCameraDeviceCatalog(Context context) : IPlatformCameraDeviceCatalog
{
    private readonly Context _context = context;

    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cameraManager = _context.GetSystemService(Context.CameraService) as CameraManager;
        if (cameraManager is null)
            return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

        var devices = new List<PlatformCameraDevice>();
        foreach (var cameraId in cameraManager.GetCameraIdList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var characteristics = cameraManager.GetCameraCharacteristics(cameraId);
            var lensFacing = characteristics?.Get(CameraCharacteristics.LensFacing) as Integer;
            var facing = lensFacing?.IntValue() switch
            {
                CameraMetadata.LensFacingFront => PlatformCameraFacing.Front,
                CameraMetadata.LensFacingBack => PlatformCameraFacing.Rear,
                _ => PlatformCameraFacing.Default
            };
            devices.Add(new PlatformCameraDevice($"android:{cameraId}", $"Camera {cameraId}",
                devices.Count, facing));
        }

        return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
    }
}
