using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Config;

namespace SecRandom.ViewModels.SettingsPages;

/// <summary>
///     随机数抽取设置页：只负责两类数字范围的初始值和重置行为。
///     名单历史与抽取模式不在这里，数字抽取也不会写名单历史。
/// </summary>
public sealed partial class RandomNumberSettingsPageViewModel : ViewModelBase
{
    private readonly MainConfigHandler _configHandler;
    private bool _normalizing;
    private bool _attached;

    public RandomNumberSettingsPageViewModel(MainConfigHandler configHandler) : base(configHandler)
    {
        _configHandler = configHandler;
        Settings = configHandler.Data.RandomNumberSettings;
    }

    public RandomNumberSettingsConfig Settings { get; }

    public void Attach()
    {
        if (_attached)
            return;

        Settings.PropertyChanged += SettingsOnPropertyChanged;
        _attached = true;
        if (Normalize())
            _configHandler.Save();
    }

    public void Detach()
    {
        if (!_attached)
            return;

        Settings.PropertyChanged -= SettingsOnPropertyChanged;
        _attached = false;
    }

    /// <summary>把持久化值夹回可抽取区间；返回是否真的改过，供调用方决定是否落盘。</summary>
    public bool Normalize()
    {
        if (_normalizing)
            return false;

        _normalizing = true;
        try
        {
            return Settings.Normalize();
        }
        finally
        {
            _normalizing = false;
        }
    }

    private void SettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_normalizing)
            return;

        Normalize();
        _configHandler.Save();
    }
}