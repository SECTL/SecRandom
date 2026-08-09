using System.Xml.Linq;
using SecRandom.Platforms.Abstractions;
using SecRandom.Platforms.Linux;
using SecRandom.Platforms.MacOs;
using SecRandom.Services.Security;

namespace SecRandom.Core.Tests.Platform;

public class RemovableStorageCatalogTests
{
    [Fact]
    public void LinuxParser_UsesParentSerialAndPartitionNumberInsteadOfDynamicDeviceName()
    {
        const string firstSnapshot = """
            { "blockdevices": [
              { "name": "sdb", "type": "disk", "rm": true, "serial": "usb-123", "children": [
                { "name": "sdb1", "type": "part", "partn": 1, "mountpoint": "/media/usb" }
              ] }
            ] }
            """;
        const string remountedSnapshot = """
            { "blockdevices": [
              { "name": "sdc", "type": "disk", "rm": true, "serial": "usb-123", "children": [
                { "name": "sdc1", "type": "part", "partn": 1, "mountpoint": "/media/usb" }
              ] }
            ] }
            """;

        var firstDevice = Assert.Single(LinuxRemovableStorageCatalog.ParseDevices(firstSnapshot));
        var remountedDevice = Assert.Single(LinuxRemovableStorageCatalog.ParseDevices(remountedSnapshot));

        Assert.Equal("media-serial:usb-123:part:1", firstDevice.DeviceId);
        Assert.Equal(firstDevice.DeviceId, remountedDevice.DeviceId);
    }

    [Fact]
    public void LinuxParser_RejectsMountedDevicesWithoutAStableIdentifier()
    {
        const string snapshot = """
            { "blockdevices": [
              { "name": "sdb", "type": "disk", "rm": true, "children": [
                { "name": "sdb1", "type": "part", "partn": 1, "mountpoint": "/media/usb" }
              ] }
            ] }
            """;

        Assert.Empty(LinuxRemovableStorageCatalog.ParseDevices(snapshot));
    }

    [Fact]
    public void MacOsParser_UsesStableDiskUuidBeforeDynamicDeviceIdentifier()
    {
        var info = XElement.Parse("""
            <dict>
              <key>DiskUUID</key><string>6DBA7D01-7166-42E6-9D2E-8E1D7386D34F</string>
              <key>DeviceIdentifier</key><string>disk4s1</string>
            </dict>
            """);

        var deviceId = MacOsRemovableStorageCatalog.GetStableDeviceId(info);

        Assert.Equal("mac-disk:6DBA7D01-7166-42E6-9D2E-8E1D7386D34F", deviceId);
    }

    [Fact]
    public void MacOsParser_RejectsDynamicDeviceIdentifierWithoutStableIdentifier()
    {
        var info = XElement.Parse("""
            <dict>
              <key>DeviceIdentifier</key><string>disk4s1</string>
            </dict>
            """);

        Assert.Null(MacOsRemovableStorageCatalog.GetStableDeviceId(info));
    }

    [Fact]
    public void UsbDeviceCatalog_WithoutDisplayLocation_DoesNotProjectTheMountRoot()
    {
        var storage = new TestRemovableStorageCatalog(new RemovableStorageDevice(
            "media-serial:usb-123:part:1",
            "Test USB",
            "/run/media/user/private-usb"));

        var device = Assert.Single(new UsbDeviceCatalog(storage).GetRemovableDevices());

        Assert.Equal(string.Empty, device.DriveLetter);
        Assert.DoesNotContain("/run/media/user/private-usb", device.DriveLetter, StringComparison.Ordinal);
    }

    private sealed class TestRemovableStorageCatalog(params RemovableStorageDevice[] devices) : IRemovableStorageCatalog
    {
        public IReadOnlyList<RemovableStorageDevice> GetReadyDevices() => devices;
    }
}
