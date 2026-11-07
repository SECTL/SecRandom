using System;
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
        Data = LoadData();
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    protected ConfigHandlerBase(ILogger logger, ConfigServiceBase configService, Func<T> fallbackFactory)
    {
        Logger = logger;
        ConfigService = configService;
        FallbackFactory = fallbackFactory;

        Logger.LogInformation("Loading config file.");
        Data = LoadData();
        Data.PropertyChanged += Data_OnPropertyChanged;
    }

    public T Data { get; private set; }
    public event EventHandler? Reloaded;

    /// <summary>
    ///     配置文件成功写入磁盘后触发。应用层用它记录整份配置的防篡改指纹，
    ///     因此必须在 <see cref="ConfigServiceBase.SaveConfig{T}" /> 之后触发。
    /// </summary>
    public event EventHandler? Saved;

    protected ILogger Logger { get; }
    protected ConfigServiceBase ConfigService { get; }
    protected Func<T> FallbackFactory { get; }

    /// <summary>
    ///     从持久化位置读取配置。默认走 <see cref="ConfigServiceBase" /> 的 JSON 文件；历史记录改写到小数据库里，
    ///     由子类覆盖这三个存储接缝，其余配置仍然共用同一套 JSON 读写。
    /// </summary>
    protected virtual T LoadData() => ConfigService.LoadConfig(FallbackFactory());

    protected virtual void SaveData() => ConfigService.SaveConfig(Data);

    protected virtual void DeleteData() => ConfigService.DeleteConfig(Data);

    public virtual void Reload()
    {
        Data.PropertyChanged -= Data_OnPropertyChanged;
        Logger.LogInformation("Reloading config file.");
        Data = LoadData();
        Data.PropertyChanged += Data_OnPropertyChanged;
        Reloaded?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Save()
    {
        Logger.LogInformation("Saving config file.");
        SaveData();
        Saved?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Delete()
    {
        Logger.LogInformation("Deleting config file.");
        DeleteData();
    }

    protected virtual void Data_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Save();
    }
}
