namespace SecRandom.Core.Abstraction.Services;

public interface IFeatureAvailabilityService
{
    /// <summary>简洁模式：只保留点名，其余功能默认隐藏并按默认配置运行。</summary>
    bool IsSimpleModeEnabled { get; }
    bool IsLotteryEnabled { get; }
    event EventHandler? Changed;
    void Refresh();
}
