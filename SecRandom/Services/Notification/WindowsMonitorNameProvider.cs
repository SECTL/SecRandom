using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;

namespace SecRandom.Services.Notification;

internal static class WindowsMonitorNameProvider
{
    private const int CchDeviceName = 32;
    private const int CchDeviceString = 128;
    private const int DisplayDeviceAttachedToDesktop = 0x00000001;

    public static IReadOnlyDictionary<PixelPoint, string> GetNames()
    {
        if (!OperatingSystem.IsWindows())
            return new Dictionary<PixelPoint, string>();

        var names = new Dictionary<PixelPoint, string>();
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapter = CreateDisplayDevice();
            if (!EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
                break;

            if ((adapter.StateFlags & DisplayDeviceAttachedToDesktop) == 0)
                continue;

            var mode = CreateDevMode();
            if (!EnumDisplaySettings(adapter.DeviceName, -1, ref mode))
                continue;

            var monitor = CreateDisplayDevice();
            if (EnumDisplayDevices(adapter.DeviceName, 0, ref monitor, 0))
            {
                var name = GetUsableName(monitor.DeviceString) ?? GetHardwareId(monitor.DeviceId);
                if (name is not null)
                    names.TryAdd(new PixelPoint(mode.PositionX, mode.PositionY), name);
            }
        }

        return names;
    }

    private static DisplayDevice CreateDisplayDevice() => new()
    {
        Size = Marshal.SizeOf<DisplayDevice>()
    };

    private static DevMode CreateDevMode() => new()
    {
        Size = (short)Marshal.SizeOf<DevMode>()
    };

    private static string? GetUsableName(string? name)
    {
        return string.IsNullOrWhiteSpace(name)
               || name.Contains("Generic PnP Monitor", StringComparison.OrdinalIgnoreCase)
            ? null
            : name;
    }

    private static string? GetHardwareId(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        var parts = deviceId.Split('\\');
        return parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : null;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplayDevices(
        string? deviceName,
        uint deviceIndex,
        ref DisplayDevice displayDevice,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplaySettings(
        string deviceName,
        int modeNumber,
        ref DevMode devMode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string DeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceString;

        public int StateFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceString)]
        public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string DeviceName;

        public short SpecVersion;
        public short DriverVersion;
        public short Size;
        public short DriverExtra;
        public int Fields;
        public int PositionX;
        public int PositionY;
        public int DisplayOrientation;
        public int DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TtOption;
        public short Collate;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchDeviceName)]
        public string FormName;

        public short LogPixels;
        public int BitsPerPel;
        public int PixelWidth;
        public int PixelHeight;
        public int DisplayFlags;
        public int DisplayFrequency;
        public int IcmMethod;
        public int IcmIntent;
        public int MediaType;
        public int DitherType;
        public int Reserved1;
        public int Reserved2;
        public int PanningWidth;
        public int PanningHeight;
    }
}
