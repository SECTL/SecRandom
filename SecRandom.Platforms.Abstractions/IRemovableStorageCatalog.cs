namespace SecRandom.Platforms.Abstractions;

public interface IRemovableStorageCatalog
{
    IReadOnlyList<RemovableStorageDevice> GetReadyDevices();
}

/// <summary>
/// Platform policy for the marker file written to a bound removable volume.
/// The file contents remain an app-layer secret; this contract only defines
/// its unified name and how the active platform hides it.
/// </summary>
public interface IRemovableStorageBindingMarker
{
    string FileName { get; }

    bool TryHide(string path);
}

/// <summary>
/// Fallback used by tests and unsupported hosts.
/// </summary>
public sealed class PortableRemovableStorageBindingMarker : IRemovableStorageBindingMarker
{
    public static PortableRemovableStorageBindingMarker Instance { get; } = new();

    private PortableRemovableStorageBindingMarker()
    {
    }

    public string FileName => ".SecRandom.safety.key";

    public bool TryHide(string path)
    {
        // Test and unsupported hosts do not promise a native hidden-file API.
        return true;
    }
}

public sealed record RemovableStorageDevice(
    string DeviceId,
    string DisplayName,
    string RootPath,
    string? DisplayLocation = null)
{
    // Optional hardware/product name for user-facing device selection. The
    // stable DeviceId remains the persistence and binding key.
    public string? HardwareName { get; init; }
}

public sealed class UnsupportedRemovableStorageCatalog : IRemovableStorageCatalog
{
    public static UnsupportedRemovableStorageCatalog Instance { get; } = new();

    private UnsupportedRemovableStorageCatalog()
    {
    }

    public IReadOnlyList<RemovableStorageDevice> GetReadyDevices() => [];
}
