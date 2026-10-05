using System.Runtime.Versioning;
using Android.Content;
using SecRandom.Mobile;
using SecRandom.Services.RosterTransfer;

namespace SecRandom.Android;

/// <summary>
///     Reclaims the full-resolution captures CameraView leaves in the Android application directories.
/// </summary>
/// <remarks>
///     The backend writes one original JPEG per shutter release (used by the QR scanner's frame fallback) into
///     the external files directory and never deletes it, so scanning repeatedly would grow the install by the
///     size of every captured photo.
/// </remarks>
[SupportedOSPlatform("android24.0")]
public sealed class AndroidCameraCaptureStore(Context context) : IMobileCameraCaptureStore
{
    private readonly Context _context = context;

    public Task<long> ClearLeftoversAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Mirrors the provider's own output choice: the external files directory when it is available,
            // otherwise the internal one.
            var directories = new[]
            {
                _context.GetExternalFilesDir(null)?.AbsolutePath,
                _context.FilesDir?.AbsolutePath
            };

            var reclaimed = RosterQrCaptureFiles.Clear(minimumAge, directories);
            if (reclaimed > 0)
                global::Android.Util.Log.Info("SecRandom.Camera",
                    $"Reclaimed {reclaimed} bytes of leftover camera captures.");

            return reclaimed;
        }, cancellationToken);
    }
}
