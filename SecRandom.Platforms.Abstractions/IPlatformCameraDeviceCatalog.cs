namespace SecRandom.Platforms.Abstractions;

/// <summary>
/// Describes one camera that can be selected for the current QR import session.
/// </summary>
public sealed record PlatformCameraDevice(
    string Id,
    string DisplayName,
    int CaptureIndex,
    PlatformCameraFacing Facing = PlatformCameraFacing.Default);

public enum PlatformCameraFacing
{
    Default,
    Front,
    Rear
}

/// <summary>
/// Enumerates the active platform's available camera devices without exposing native APIs to the app layer.
/// </summary>
public interface IPlatformCameraDeviceCatalog
{
    Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken);
}

public sealed class UnsupportedPlatformCameraDeviceCatalog : IPlatformCameraDeviceCatalog
{
    public static UnsupportedPlatformCameraDeviceCatalog Instance { get; } = new();

    private UnsupportedPlatformCameraDeviceCatalog()
    {
    }

    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);
}
