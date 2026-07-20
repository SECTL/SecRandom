using Microsoft.Extensions.DependencyInjection;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Abstraction.Services;

namespace SecRandom.Mobile.Views;

/// <summary>
/// 移动端能力投影的统一消费入口：页面不直接读 IAppHost / OperatingSystem，
/// 一律经由这里的只读投影判断平台与功能开关。
/// </summary>
internal static class MobileCapabilities
{
    internal static bool IsAndroid => OperatingSystem.IsAndroid();

    // 应用内更新目前只有 Android 头实现了系统安装器投影（IMobileUpdateInstaller），iOS 分发仍属延期项。
    internal static bool SupportsInAppUpdate => OperatingSystem.IsAndroid();

    internal static bool IsLotteryEnabled => FeatureAvailability?.IsLotteryEnabled ?? true;

    // IAppHost.Host 是过渡性 Core 消费入口，由 MobileApp 在 Host 建立后赋值、退出时清空；
    // Host 未就绪时按功能可用处理，避免预热期误隐藏入口。
    internal static IFeatureAvailabilityService? FeatureAvailability =>
        IAppHost.Host?.Services.GetService<IFeatureAvailabilityService>();
}
