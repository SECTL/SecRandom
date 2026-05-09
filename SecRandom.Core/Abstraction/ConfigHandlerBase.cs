using System.ComponentModel;
using Microsoft.Extensions.Logging;
using SecRandom.Shared.Abstraction;

namespace SecRandom.Core.Abstraction;

public abstract class ConfigHandlerBase<T> where T : ConfigBase
{
    protected ConfigHandlerBase(Func<T> fallbackFactory)
    {
        Logger = (ILogger)IAppHost.Host?.Services.GetService(typeof(ILogger<>).MakeGenericType(GetType()))!;
        ConfigService = IAppHost.GetService<ConfigServiceBase>();
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    protected ConfigHandlerBase(ILogger logger, ConfigServiceBase configService, Func<T> fallbackFactory)
    {
        Logger = logger;
        ConfigService = configService;
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    public T Data { get; private set; }

    private ILogger Logger { get; }
    private ConfigServiceBase ConfigService { get; }
    private Func<T> FallbackFactory { get; }

    public virtual void Reload()
    {
        Data.PropertyChanged -= Data_OnPropertyChanged;
        Logger.LogInformation("Reloading config file.");
        Data = ConfigService.LoadConfig(FallbackFactory());
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    public virtual void Save()
    {
        Logger.LogInformation("Saving config file.");
        ConfigService.SaveConfig(Data);
    }

    public virtual void Delete()
    {
        Logger.LogInformation("Deleting config file.");
        ConfigService.DeleteConfig(Data);
    }

    protected virtual void Data_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }
}