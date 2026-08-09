using SecRandom.Core.Abstraction.Services;
using SecRandom.Mobile;

namespace SecRandom.Views.Mobile;

/// <summary>
/// Stable mobile navigation keys. Platform composition chooses which keys have a keyed control registration.
/// These keys do not select alternate view types at runtime.
/// </summary>
public static class MobilePageIds
{
    public const string Root = "root.mobile";
    public const string Draw = "main.rollCall";
    public const string History = "main.history";
    public const string Overview = "main.overview";
    public const string Settings = "root.settings";
    public const string Update = "settings.mobile.update";
}

public enum MobileDestination
{
    Draw,
    History,
    Overview,
    Settings
}

/// <summary>
/// Read-only mobile capability projection selected by platform DI at startup.
/// </summary>
public interface IMobileCapabilities
{
    bool IsLotteryEnabled { get; }
    bool SupportsInAppUpdate { get; }
}

internal sealed class MobileCapabilities(
    IFeatureAvailabilityService featureAvailability,
    IMobileUpdateInstaller updateInstaller) : IMobileCapabilities
{
    public bool IsLotteryEnabled => featureAvailability.IsLotteryEnabled;
    public bool SupportsInAppUpdate => updateInstaller.IsSupported;
}

internal enum DrawSurface
{
    RollCall,
    Lottery
}
