using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Services.Config;
using SecRandom.Core.Models;
using SecRandom.Shared.Abstraction;
using SecRandom.Shared.Models.Profile;

namespace SecRandom.Services.Config;

public class DesktopConfigService(ILogger<DesktopConfigService> logger) : ConfigServiceBase
{
    private ILogger<DesktopConfigService> Logger { get; } = logger;
    private EncryptedProfileHistoryStore? _historyStore;
    private EncryptedProfileJsonStore? _profileJsonStore;

    private EncryptedProfileHistoryStore HistoryStore =>
        _historyStore ??= IAppHost.GetService<EncryptedProfileHistoryStore>();

    private EncryptedProfileJsonStore ProfileJsonStore =>
        _profileJsonStore ??= IAppHost.GetService<EncryptedProfileJsonStore>();

    public override bool IsConfigExists<T>(T fallback)
    {
        if (fallback is StudentList studentList)
            return ProfileJsonStore.Exists(studentList);
        if (fallback is PrizeList prizeList)
            return ProfileJsonStore.Exists(prizeList);
        if (fallback is StudentHistory studentHistory)
            return HistoryStore.Exists(studentHistory);
        if (fallback is PrizeHistory prizeHistory)
            return HistoryStore.Exists(prizeHistory);

        var filePath = fallback.ConfigFilePath;
        Logger.LogInformation("Checking config file existence: {Path}", filePath);

        return File.Exists(filePath);
    }

    public override T LoadConfig<T>(T fallback)
    {
        if (fallback is StudentList studentList)
            return (T)(object)ProfileJsonStore.Load(studentList);
        if (fallback is PrizeList prizeList)
            return (T)(object)ProfileJsonStore.Load(prizeList);
        if (fallback is StudentHistory studentHistory)
            return (T)HistoryStore.Load(studentHistory);
        if (fallback is PrizeHistory prizeHistory)
            return (T)HistoryStore.Load(prizeHistory);

        var filePath = fallback.ConfigFilePath;
        Logger.LogInformation("Loading config file: {Path}", filePath);
        return ProfileEncryptionCodec.LoadJsonFile(filePath, fallback, GetPurpose(typeof(T)), IsLegacyConfigCandidate);
    }

    public override void SaveConfig<T>(T config)
    {
        if (config is StudentList studentList)
        {
            ProfileJsonStore.Save(studentList);
            return;
        }

        if (config is PrizeList prizeList)
        {
            ProfileJsonStore.Save(prizeList);
            return;
        }

        if (config is StudentHistory studentHistory)
        {
            HistoryStore.Save(studentHistory);
            return;
        }

        if (config is PrizeHistory prizeHistory)
        {
            HistoryStore.Save(prizeHistory);
            return;
        }

        var filePath = config.ConfigFilePath;
        Logger.LogInformation("Saving config file: {Path}", filePath);
        ProfileEncryptionCodec.SaveJsonFile(filePath, config, GetPurpose(config.GetType()));
    }

    public override void DeleteConfig<T>(T config)
    {
        if (config is StudentList studentList)
        {
            ProfileJsonStore.Delete(studentList);
            return;
        }

        if (config is PrizeList prizeList)
        {
            ProfileJsonStore.Delete(prizeList);
            return;
        }

        if (config is StudentHistory studentHistory)
        {
            HistoryStore.Delete(studentHistory);
            return;
        }

        if (config is PrizeHistory prizeHistory)
        {
            HistoryStore.Delete(prizeHistory);
            return;
        }

        var filePath = config.ConfigFilePath;
        Logger.LogInformation("Deleting config file: {Path}", filePath);

        if (!File.Exists(filePath)) return;
        File.Delete(filePath);
    }

    private static string GetPurpose(Type configType)
    {
        return configType == typeof(MainConfigModel)
            ? "SecRandom.MainConfig.v1"
            : $"SecRandom.Config.{configType.FullName ?? configType.Name}.v1";
    }

    private static bool IsLegacyConfigCandidate(System.Text.Json.JsonElement root)
    {
        return root.TryGetProperty("general", out _)
               || root.TryGetProperty("appearance", out _)
               || root.TryGetProperty("basic", out _)
               || root.TryGetProperty("backup", out _)
               || root.TryGetProperty("float_position", out _);
    }
}
