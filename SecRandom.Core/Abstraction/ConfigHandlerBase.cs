using System.ComponentModel;
using Microsoft.Extensions.Logging;

namespace SecRandom.Core.Abstraction;

public abstract class ConfigHandlerBase<T> where T : ConfigBase
{
    public T Data { get; private set; }
    
    private ILogger Logger { get; }
    private ConfigServiceBase ConfigService { get; }
    private Func<T> FallbackFactory { get; }
    
    protected ConfigHandlerBase(ILogger logger, ConfigServiceBase configService, Func<T> fallbackFactory)
    {
        Logger = logger;
        ConfigService = configService;
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("加载配置文件...");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    public virtual void Reload()
    {
        Data.PropertyChanged -= Data_OnPropertyChanged;
        Logger.LogInformation("重新加载配置文件...");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }
    
    public virtual void Save()
    {
        Logger.LogInformation("保存配置文件...");
        ConfigService.SaveConfig(Data);
    }
    
    public virtual void Delete()
    {
        Logger.LogInformation("删除配置文件...");
        ConfigService.DeleteConfig(Data);
    }
    
    protected virtual void Data_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }
}