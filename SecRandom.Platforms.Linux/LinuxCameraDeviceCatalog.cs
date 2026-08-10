using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Linux;

public sealed class LinuxCameraDeviceCatalog : IPlatformCameraDeviceCatalog
{
    private const string VideoDeviceDirectory = "/sys/class/video4linux";

    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(VideoDeviceDirectory))
            return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

        var devices = new List<PlatformCameraDevice>();
        foreach (var directory in Directory.EnumerateDirectories(VideoDeviceDirectory, "video*")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(directory);
            if (!int.TryParse(fileName.AsSpan("video".Length), out var captureIndex) ||
                !File.Exists($"/dev/{fileName}"))
            {
                continue;
            }

            var namePath = Path.Combine(directory, "name");
            var name = File.Exists(namePath) ? File.ReadAllText(namePath).Trim() : fileName;
            devices.Add(new PlatformCameraDevice($"v4l2:/dev/{fileName}",
                string.IsNullOrWhiteSpace(name) ? fileName : name, captureIndex));
        }

        return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
    }
}
