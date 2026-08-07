using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Services.Security;

internal interface IUsbDeviceCatalog
{
    IReadOnlyList<UsbDriveInfo> GetRemovableDevices();
}

internal sealed record UsbDriveInfo(string DriveLetter, string DisplayName, string DeviceId, string RootPath)
{
    public string? HardwareName { get; init; }
}

internal sealed class UsbDeviceCatalog(IRemovableStorageCatalog removableStorage) : IUsbDeviceCatalog
{
    public IReadOnlyList<UsbDriveInfo> GetRemovableDevices()
    {
        var result = new List<UsbDriveInfo>();
        IReadOnlyList<RemovableStorageDevice> devices;
        try
        {
            devices = removableStorage.GetReadyDevices();
        }
        catch (IOException)
        {
            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceId) || string.IsNullOrWhiteSpace(device.RootPath))
                continue;

            try
            {
                var rootPath = Path.GetFullPath(device.RootPath);
                var displayLocation = GetDisplayLocation(device);
                result.Add(new UsbDriveInfo(
                    displayLocation,
                    string.IsNullOrWhiteSpace(device.DisplayName) ? displayLocation : device.DisplayName,
                    device.DeviceId,
                    rootPath)
                {
                    HardwareName = device.HardwareName
                });
            }
            catch (ArgumentException)
            {
            }
            catch (IOException)
            {
            }
            catch (NotSupportedException)
            {
            }
        }

        return result;
    }

    private static string GetDisplayLocation(RemovableStorageDevice device) =>
        device.DisplayLocation ?? string.Empty;
}
