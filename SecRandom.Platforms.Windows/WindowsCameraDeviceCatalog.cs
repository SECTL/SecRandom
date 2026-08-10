using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.Windows;

/// <summary>
/// Reads DirectShow video-input monikers so Windows shows the same physical and virtual camera names as other desktop apps.
/// </summary>
public sealed class WindowsCameraDeviceCatalog : IPlatformCameraDeviceCatalog
{
    private static readonly Guid VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");

    public Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
            return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>([]);

        var devices = new List<PlatformCameraDevice>();
        object? systemDeviceEnumerator = null;
        IEnumMoniker? enumerator = null;
        try
        {
            systemDeviceEnumerator = new SystemDeviceEnumerator();
            var createDeviceEnumerator = (ICreateDeviceEnumerator)systemDeviceEnumerator;
            var category = VideoInputDeviceCategory;
            if (createDeviceEnumerator.CreateClassEnumerator(ref category, out enumerator, 0) != 0 ||
                enumerator is null)
            {
                return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
            }

            var monikers = new IMoniker[1];
            var captureIndex = 0;
            while (enumerator.Next(1, monikers, IntPtr.Zero) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var moniker = monikers[0];
                try
                {
                    var name = GetFriendlyName(moniker);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    devices.Add(new PlatformCameraDevice($"dshow:{captureIndex}", name, captureIndex));
                    captureIndex++;
                }
                finally
                {
                    Marshal.FinalReleaseComObject(moniker);
                    monikers[0] = null!;
                }
            }
        }
        catch (COMException)
        {
            devices.Clear();
        }
        finally
        {
            if (enumerator is not null)
                Marshal.FinalReleaseComObject(enumerator);
            if (systemDeviceEnumerator is not null)
                Marshal.FinalReleaseComObject(systemDeviceEnumerator);
        }

        return Task.FromResult<IReadOnlyList<PlatformCameraDevice>>(devices);
    }

    [SupportedOSPlatform("windows")]
    private static string? GetFriendlyName(IMoniker moniker)
    {
        var propertyBagId = typeof(IPropertyBag).GUID;
        object? propertyBagObject = null;
        try
        {
            moniker.BindToStorage(null!, null!, ref propertyBagId, out propertyBagObject);
            if (propertyBagObject is not IPropertyBag propertyBag ||
                propertyBag.Read("FriendlyName", out var value, IntPtr.Zero) != 0)
            {
                return null;
            }

            return value as string;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (propertyBagObject is not null && Marshal.IsComObject(propertyBagObject))
                Marshal.FinalReleaseComObject(propertyBagObject);
        }
    }

    [ComImport]
    [Guid("62BE5D10-60EB-11D0-BD3B-00A0C911CE86")]
    private class SystemDeviceEnumerator
    {
    }

    [ComImport]
    [Guid("29840822-5B84-11D0-BD3B-00A0C911CE86")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICreateDeviceEnumerator
    {
        [PreserveSig]
        int CreateClassEnumerator(ref Guid category, out IEnumMoniker? enumerator, int flags);
    }

    [ComImport]
    [Guid("55272A00-42CB-11CE-8135-00AA004BB851")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyBag
    {
        [PreserveSig]
        int Read([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] out object value,
            IntPtr errorLog);

        [PreserveSig]
        int Write([MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.Struct)] ref object value);
    }
}
