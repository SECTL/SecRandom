using System.Runtime.Versioning;
using Android.Content;
using Android.Hardware.Camera2;
using Java.Lang;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Android;

[SupportedOSPlatform("android24.0")]
public sealed class AndroidCameraDeviceCatalog(Context context) : IPlatformCameraDeviceCatalog
{
    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.GetSystemService(Context.CameraService) is not CameraManager cameraManager)
            return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

        var devices = new List<PlatformCameraDevice>();
        foreach (var (index, cameraId) in cameraManager.GetCameraIdList().Index())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var characteristics = cameraManager.GetCameraCharacteristics(cameraId);
            var lensFacing = characteristics.Get(CameraCharacteristics.LensFacing) as Integer;
            var facing = (LensFacing)lensFacing!.IntValue() switch
            {
                LensFacing.Front => PlatformCameraFacing.Front,
                LensFacing.Back => PlatformCameraFacing.Rear,
                _ => PlatformCameraFacing.Default
            };
            devices.Add(new PlatformCameraDevice($"android:{cameraId}", $"Camera {cameraId}", index, facing));
        }

        return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
    }
}
