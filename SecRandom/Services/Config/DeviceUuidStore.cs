using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Shared;

namespace SecRandom.Services.Config;

public sealed class DeviceUuidStore(MainConfigHandler configHandler, ILogger<DeviceUuidStore> logger)
{
    private readonly object _syncRoot = new();
    private Guid? _deviceUuid;

    public Guid GetOrCreate()
    {
        lock (_syncRoot)
        {
            if (_deviceUuid is { } cachedUuid)
                return cachedUuid;

            Guid deviceUuid;
            var path = Utils.GetFilePath("config", "device-uuid.json");
            if (TryRead(path, out deviceUuid))
            {
                _deviceUuid = deviceUuid;
                return deviceUuid;
            }

            var legacyUuid = GetLegacyUuid(configHandler.Data.General.Basic);
            deviceUuid = legacyUuid;
            if (deviceUuid == Guid.Empty)
                deviceUuid = Guid.NewGuid();

            if (TryWrite(path, deviceUuid) && legacyUuid != Guid.Empty)
                configHandler.Save();

            _deviceUuid = deviceUuid;
            return deviceUuid;
        }
    }

    public void Reload()
    {
        lock (_syncRoot)
            _deviceUuid = null;
    }

    private static bool TryRead(string path, out Guid deviceUuid)
    {
        try
        {
            if (!File.Exists(path))
            {
                deviceUuid = Guid.Empty;
                return false;
            }

            var file = JsonSerializer.Deserialize<DeviceUuidFile>(File.ReadAllText(path));
            deviceUuid = file?.Uuid ?? Guid.Empty;
            return deviceUuid != Guid.Empty;
        }
        catch (Exception)
        {
            deviceUuid = Guid.Empty;
            return false;
        }
    }

    private bool TryWrite(string path, Guid deviceUuid)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new DeviceUuidFile(deviceUuid)));
            File.Move(temporaryPath, path, true);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to persist device UUID: {Path}", path);
            return false;
        }
    }

    private static Guid GetLegacyUuid(BasicSettingsConfig basicSettings) => basicSettings.LegacyOfflineUserId;

    private sealed record DeviceUuidFile(Guid Uuid);
}
