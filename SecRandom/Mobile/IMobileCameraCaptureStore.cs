namespace SecRandom.Mobile;

/// <summary>
///     Reclaims the temporary camera files a native camera backend writes outside the application data root.
/// </summary>
/// <remarks>
///     CameraView's Android provider saves one full-resolution JPEG per shutter release under the app's
///     external files directory and never removes it, so a QR scan session would otherwise leave original
///     images on the device forever. Heads whose backend composes every frame in memory (iOS) or that have no
///     camera at all keep <see cref="UnsupportedMobileCameraCaptureStore" />.
/// </remarks>
public interface IMobileCameraCaptureStore
{
    /// <summary>Deletes the camera backend's capture files that are at least <paramref name="minimumAge" /> old.</summary>
    /// <remarks>
    ///     Implementations must not block the calling thread: the QR capture session and the mobile startup path
    ///     both call this from the UI thread. A call site that cannot race with a capture the backend still has
    ///     to read passes <see cref="TimeSpan.Zero" /> and reclaims every capture file.
    /// </remarks>
    /// <returns>The number of bytes reclaimed.</returns>
    Task<long> ClearLeftoversAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default);
}

/// <summary>
///     No-op store for heads that keep every captured frame in memory.
/// </summary>
public sealed class UnsupportedMobileCameraCaptureStore : IMobileCameraCaptureStore
{
    public static UnsupportedMobileCameraCaptureStore Instance { get; } = new();

    public Task<long> ClearLeftoversAsync(TimeSpan minimumAge, CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);
}
