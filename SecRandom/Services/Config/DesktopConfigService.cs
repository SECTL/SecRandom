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
        Logger.LogInformation("在 {PATH} 判断配置...", filePath);

        return File.Exists(filePath);
    }
    
    public override T LoadConfig<T>(T fallback)
    {
        var filePath = fallback.ConfigFilePath;
        Logger.LogInformation("从 {PATH} 加载配置...", filePath);
        
        if (!File.Exists(filePath))
        {
            Logger.LogWarning("加载失败，正在回滚并保存...");
            SaveConfig(fallback);
            return fallback;
        }
        
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch
        {
            Logger.LogWarning("加载失败，正在回滚并保存...");
            SaveConfig(fallback);
            return fallback;
        }
    }

    public override void SaveConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        Logger.LogInformation("往 {PATH} 保存配置...", filePath);
        
        var json = JsonSerializer.Serialize(config, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public override void DeleteConfig<T>(T config)
    {
        var filePath = config.ConfigFilePath;
        Logger.LogInformation("在 {PATH} 删除配置...", filePath);
        
        if (!File.Exists(filePath)) return;
        File.Delete(filePath);
    }
}