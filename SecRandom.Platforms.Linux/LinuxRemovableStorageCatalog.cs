using System.Text.Json;
using SecRandom.Platforms.Abstractions;
using SecRandom.Platforms;

namespace SecRandom.Platforms.Linux;

internal sealed class LinuxRemovableStorageCatalog : IRemovableStorageCatalog
{
    public IReadOnlyList<RemovableStorageDevice> GetReadyDevices()
    {
        try
        {
            var startInfo = CreateLsblkStartInfo("NAME,TYPE,MOUNTPOINT,MOUNTPOINTS,UUID,PARTUUID,PARTN,RM,HOTPLUG,LABEL,SERIAL,WWN");
            if (!PlatformProcessRunner.TryGetOutput(startInfo, TimeSpan.FromSeconds(5), out var output))
            {
                // MOUNTPOINTS/HOTPLUG are not available on older util-linux releases.
                startInfo = CreateLsblkStartInfo("NAME,TYPE,MOUNTPOINT,UUID,PARTUUID,PARTN,RM,LABEL,SERIAL,WWN");
                if (!PlatformProcessRunner.TryGetOutput(startInfo, TimeSpan.FromSeconds(5), out output))
                    return [];
            }

            if (string.IsNullOrWhiteSpace(output))
                return [];

            return ParseDevices(output);
        }
        catch (JsonException)
        {
            return [];
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return [];
        }
        catch (InvalidOperationException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static System.Diagnostics.ProcessStartInfo CreateLsblkStartInfo(string columns) => new()
    {
        FileName = "lsblk",
        Arguments = $"--json --output {columns}",
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    internal static IReadOnlyList<RemovableStorageDevice> ParseDevices(string output)
    {
        using var document = JsonDocument.Parse(output);
        if (!document.RootElement.TryGetProperty("blockdevices", out var devices))
            return [];

        var result = new List<RemovableStorageDevice>();
        CollectDevices(devices, removableParent: false, MediaIdentity.Empty, result);
        return result;
    }

    private static void CollectDevices(
        JsonElement devices,
        bool removableParent,
        MediaIdentity parentIdentity,
        ICollection<RemovableStorageDevice> result)
    {
        foreach (var device in devices.EnumerateArray())
        {
            var removable = removableParent || GetBoolean(device, "rm") || GetBoolean(device, "hotplug");
            var mediaIdentity = parentIdentity.Combine(device);
            var mountPoint = GetMountPoint(device);
            var deviceId = GetDeviceId(device, mediaIdentity);
            if (removable && !string.IsNullOrWhiteSpace(mountPoint) && deviceId is not null)
            {
                var label = GetString(device, "label");
                var name = GetString(device, "name");
                result.Add(new RemovableStorageDevice(
                    deviceId,
                    string.IsNullOrWhiteSpace(label) ? name ?? deviceId : label,
                    Path.GetFullPath(mountPoint),
                    string.IsNullOrWhiteSpace(name) ? deviceId : name));
            }

            if (device.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
                CollectDevices(children, removable, mediaIdentity, result);
        }
    }

    private static string? GetDeviceId(JsonElement element, MediaIdentity mediaIdentity)
    {
        var partUuid = GetString(element, "partuuid");
        if (!string.IsNullOrWhiteSpace(partUuid))
            return $"part-uuid:{partUuid}";

        var uuid = GetString(element, "uuid");
        if (!string.IsNullOrWhiteSpace(uuid))
            return $"fs-uuid:{uuid}";

        var type = GetString(element, "type");
        var partition = type == "part" ? GetString(element, "partn") : null;
        if (type == "part" && string.IsNullOrWhiteSpace(partition))
            return null;

        var suffix = string.IsNullOrWhiteSpace(partition) ? string.Empty : $":part:{partition}";
        if (!string.IsNullOrWhiteSpace(mediaIdentity.Serial))
            return $"media-serial:{mediaIdentity.Serial}{suffix}";
        return !string.IsNullOrWhiteSpace(mediaIdentity.Wwn)
            ? $"media-wwn:{mediaIdentity.Wwn}{suffix}"
            : null;
    }

    private static string? GetMountPoint(JsonElement element)
    {
        var mountPoint = GetString(element, "mountpoint");
        if (!string.IsNullOrWhiteSpace(mountPoint))
            return mountPoint;

        if (!element.TryGetProperty("mountpoints", out var mountPoints) ||
            mountPoints.ValueKind != JsonValueKind.Array)
            return null;

        return mountPoints.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static bool GetBoolean(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;
        if (value.ValueKind == JsonValueKind.True)
            return true;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number != 0;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private readonly record struct MediaIdentity(string? Serial, string? Wwn)
    {
        public static MediaIdentity Empty { get; } = new(null, null);

        public MediaIdentity Combine(JsonElement element) => new(
            GetString(element, "serial") ?? Serial,
            GetString(element, "wwn") ?? Wwn);
    }
}
