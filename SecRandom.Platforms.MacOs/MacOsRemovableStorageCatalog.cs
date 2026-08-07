using System.Xml.Linq;
using SecRandom.Platforms.Abstractions;
using SecRandom.Platforms;

namespace SecRandom.Platforms.MacOs;

internal sealed class MacOsRemovableStorageCatalog : IRemovableStorageCatalog
{
    public IReadOnlyList<RemovableStorageDevice> GetReadyDevices()
    {
        var result = new List<RemovableStorageDevice>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady || !TryReadInfo(drive.RootDirectory.FullName, out var info) ||
                    !(GetBoolean(info, "RemovableMedia") ||
                      GetBoolean(info, "ExternalDisk") ||
                      GetBoolean(info, "Ejectable")))
                    continue;

                var rootPath = GetString(info, "MountPoint");
                if (string.IsNullOrWhiteSpace(rootPath))
                    continue;

                var deviceId = GetStableDeviceId(info);
                if (deviceId is null)
                    continue;

                var displayLocation = GetString(info, "DeviceIdentifier") ?? deviceId;
                var displayName = GetString(info, "VolumeName") ?? displayLocation;
                result.Add(new RemovableStorageDevice(
                    deviceId,
                    displayName,
                    Path.GetFullPath(rootPath),
                    displayLocation));
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ArgumentException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return result;
    }

    private static bool TryReadInfo(string rootPath, out XElement dictionary)
    {
        dictionary = null!;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "diskutil",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("info");
            startInfo.ArgumentList.Add("-plist");
            startInfo.ArgumentList.Add(rootPath);
            if (!PlatformProcessRunner.TryGetOutput(startInfo, TimeSpan.FromSeconds(5), out var output))
                return false;

            dictionary = XDocument.Parse(output).Root?.Element("dict")!;
            return dictionary is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string? GetString(XElement dictionary, string key)
    {
        var keyElement = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
        return keyElement?.ElementsAfterSelf().FirstOrDefault()?.Value;
    }

    internal static string? GetStableDeviceId(XElement dictionary)
    {
        var volumeUuid = GetString(dictionary, "VolumeUUID");
        if (!string.IsNullOrWhiteSpace(volumeUuid))
            return $"mac-volume:{volumeUuid}";

        var diskUuid = GetString(dictionary, "DiskUUID");
        if (!string.IsNullOrWhiteSpace(diskUuid))
            return $"mac-disk:{diskUuid}";

        var mediaUuid = GetString(dictionary, "MediaUUID");
        return string.IsNullOrWhiteSpace(mediaUuid) ? null : $"mac-media:{mediaUuid}";
    }

    private static bool GetBoolean(XElement dictionary, string key)
    {
        var keyElement = dictionary.Elements("key").FirstOrDefault(element => element.Value == key);
        return keyElement?.ElementsAfterSelf().FirstOrDefault()?.Name.LocalName == "true";
    }
}
