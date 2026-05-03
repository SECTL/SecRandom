using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;

namespace SecRandom.Services.Config;

public class DesktopConfigService(ILogger<DesktopConfigService> logger) : ConfigServiceBase
{
    private ILogger<DesktopConfigService> Logger { get; } = logger;
    
    public override bool IsConfigExists<T>(T fallback)
    {
        var filePath = fallback.ConfigFilePath;
        Logger.LogInformation("Checking config file existence: {Path}", filePath);

        return File.Exists(filePath);
    }
    
    public override T LoadConfig<T>(T fallback)
    {
        var filePath = fallback.ConfigFilePath;
        Logger.LogInformation("Loading config file: {Path}", filePath);
        
        if (!File.Exists(filePath))
        {
            Logger.LogWarning("Config file does not exist; saving fallback config.");
            SaveConfig(fallback);
            return fallback;
        }
        
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to load config file; saving fallback config: {Path}", filePath);
            SaveConfig(fallback);
            return fallback;
        }
    }

    public override void SaveConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        Logger.LogInformation("Saving config file: {Path}", filePath);
        
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public override void DeleteConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        Logger.LogInformation("Deleting config file: {Path}", filePath);
        
        if (!File.Exists(filePath)) return;
        File.Delete(filePath);
    }
}
