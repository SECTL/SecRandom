using System.Text.Json;
using SecRandom.Shared;

namespace SecRandom.Services.Mobile;

internal sealed class MobileDeviceUuidStore
{
    private readonly object _sync = new();
    private Guid? _deviceId;

    public Guid GetOrCreate()
    {
        lock (_sync)
        {
            if (_deviceId is { } cached)
                return cached;

            var path = Utils.GetFilePath("config", "device-uuid.json");
            try
            {
                var file = File.Exists(path)
                    ? JsonSerializer.Deserialize(File.ReadAllText(path), MobileJsonContext.Default.DeviceUuidFile)
                    : null;
                if (file is not null && file.Uuid != Guid.Empty)
                {
                    _deviceId = file.Uuid;
                    return file.Uuid;
                }
            }
            catch (Exception)
            {
                // A malformed local identifier is replaced with a new opaque identifier.
            }

            var created = Guid.NewGuid();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new DeviceUuidFile(created), MobileJsonContext.Default.DeviceUuidFile));
            File.Move(temporary, path, true);
            _deviceId = created;
            return created;
        }
    }
}
