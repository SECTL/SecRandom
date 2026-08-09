using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

internal sealed class WindowsRemovableStorageCatalog : IRemovableStorageCatalog
{
    public IReadOnlyList<RemovableStorageDevice> GetReadyDevices()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Select(CreateIfReadyRemovable)
                .OfType<RemovableStorageDevice>()
                .ToList();
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

    private static RemovableStorageDevice? CreateIfReadyRemovable(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
                return null;

            var rootPath = Path.GetFullPath(drive.RootDirectory.FullName);
            // Windows reports some USB flash drives and portable SSDs as Fixed.
            // Keep the DriveInfo check for ordinary removable media, but query
            // the storage bus before accepting those fixed volumes as USB.
            var usbInfo = TryGetUsbStorageInfo(rootPath);
            if (drive.DriveType != DriveType.Removable && !usbInfo.IsUsb)
                return null;

            var displayLocation = GetDisplayLocation(rootPath);
            var displayName = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? displayLocation : drive.VolumeLabel;
            var volumeName = new StringBuilder(260);
            var deviceId = GetVolumeNameForVolumeMountPoint(rootPath, volumeName, (uint)volumeName.Capacity)
                ? $"volume-guid:{volumeName.ToString().TrimEnd('\0')}"
                : TryGetVolumeSerialNumber(rootPath);
            if (deviceId is null)
                return null;

            return new RemovableStorageDevice(deviceId, displayName, rootPath, displayLocation)
            {
                HardwareName = usbInfo.HardwareName
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string GetDisplayLocation(string rootPath) =>
        rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? TryGetVolumeSerialNumber(string rootPath)
    {
        return GetVolumeInformation(
            rootPath,
            null,
            0,
            out var serialNumber,
            out _,
            out _,
            null,
            0)
            ? $"volume-serial:{serialNumber:X8}"
            : null;
    }

    private static UsbStorageInfo TryGetUsbStorageInfo(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
            return UsbStorageInfo.Empty;

        var volumeName = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (volumeName.Length != 2 || volumeName[1] != ':')
            return UsbStorageInfo.Empty;

        using var volumeHandle = CreateFile(
            $@"\\.\{volumeName}",
            0,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);
        if (volumeHandle.IsInvalid)
            return UsbStorageInfo.Empty;

        // A volume handle may return a valid descriptor without exposing the
        // underlying USB bus. Continue with the disk-extents lookup in that
        // case so fixed-type USB media is still discovered.
        if (TryGetStorageDescriptor(volumeHandle, out var descriptor) &&
            descriptor.BusType == BusTypeUsb)
            return new UsbStorageInfo(true, descriptor.ProductId);

        var extents = Marshal.AllocHGlobal(VolumeDiskExtentsSize);
        try
        {
            if (!DeviceIoControl(
                    volumeHandle,
                    IoctlVolumeGetVolumeDiskExtents,
                    IntPtr.Zero,
                    0,
                    extents,
                    VolumeDiskExtentsSize,
                    out _,
                    IntPtr.Zero))
                return UsbStorageInfo.Empty;

            // VOLUME_DISK_EXTENTS aligns the first DISK_EXTENT to 8 bytes:
            // DWORD NumberOfDiskExtents (4 bytes) + 4 bytes padding.
            var diskNumber = Marshal.ReadInt32(extents, FirstExtentOffset);
            using var physicalHandle = CreateFile(
                $@"\\.\PhysicalDrive{diskNumber}",
                0,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);
            if (physicalHandle.IsInvalid || !TryGetStorageDescriptor(physicalHandle, out descriptor) ||
                descriptor.BusType != BusTypeUsb)
                return UsbStorageInfo.Empty;

            return new UsbStorageInfo(true, descriptor.ProductId);
        }
        catch (IOException)
        {
            return UsbStorageInfo.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return UsbStorageInfo.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(extents);
        }
    }

    private static bool TryGetStorageDescriptor(
        SafeFileHandle deviceHandle,
        out StorageDescriptorInfo descriptorInfo)
    {
        descriptorInfo = default;
        var query = Marshal.AllocHGlobal(StoragePropertyQuerySize);
        var descriptor = Marshal.AllocHGlobal(StorageDescriptorBufferSize);
        try
        {
            Marshal.Copy(new byte[StoragePropertyQuerySize], 0, query, StoragePropertyQuerySize);
            Marshal.WriteInt32(query, 0, (int)StorageDeviceProperty);
            Marshal.WriteInt32(query, sizeof(uint), (int)PropertyStandardQuery);
            if (!DeviceIoControl(
                    deviceHandle,
                    IoctlStorageQueryProperty,
                    query,
                    StoragePropertyQuerySize,
                    descriptor,
                    StorageDescriptorBufferSize,
                    out var returned,
                    IntPtr.Zero) || returned <= BusTypeOffset)
                return false;

            descriptorInfo = new StorageDescriptorInfo(
                Marshal.ReadByte(descriptor, BusTypeOffset),
                ReadDescriptorString(descriptor, returned, ProductIdOffset));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(query);
            Marshal.FreeHGlobal(descriptor);
        }
    }

    private static string? ReadDescriptorString(IntPtr descriptor, int returned, int offsetLocation)
    {
        if (returned <= offsetLocation)
            return null;

        var offset = Marshal.ReadInt32(descriptor, offsetLocation);
        if (offset <= 0 || offset >= returned)
            return null;

        return Marshal.PtrToStringAnsi(IntPtr.Add(descriptor, offset))?.Trim();
    }

    private readonly record struct UsbStorageInfo(bool IsUsb, string? HardwareName)
    {
        public static UsbStorageInfo Empty { get; } = new(false, null);
    }

    private readonly record struct StorageDescriptorInfo(byte BusType, string? ProductId);

    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const int StoragePropertyQuerySize = 12;
    private const int StorageDescriptorBufferSize = 1024;
    private const int VolumeDiskExtentsSize = 32;
    private const int FirstExtentOffset = 8;
    private const uint StorageDeviceProperty = 0;
    private const uint PropertyStandardQuery = 0;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const int BusTypeOffset = 28;
    private const int ProductIdOffset = 16;
    private const byte BusTypeUsb = 7;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle deviceHandle,
        uint ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out int bytesReturned,
        IntPtr overlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string lpszVolumeMountPoint,
        StringBuilder lpszVolumeName,
        uint cchBufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);
}
