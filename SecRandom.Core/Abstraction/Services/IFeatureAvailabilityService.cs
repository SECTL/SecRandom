namespace SecRandom.Core.Abstraction.Services;

public interface IFeatureAvailabilityService
{
    bool IsLotteryEnabled { get; }
    event EventHandler? Changed;
    void Refresh();
}
