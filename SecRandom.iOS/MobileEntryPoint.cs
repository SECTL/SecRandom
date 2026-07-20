#if IOS
using Avalonia;
using Avalonia.iOS;
using Foundation;
using SecRandom.Core.Abstraction;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Telemetry;
using System.Runtime.Versioning;
using UIKit;

namespace SecRandom.Mobile.iOS;

[SupportedOSPlatform("ios13.0")]
public static class MobileEntryPoint
{
    public static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}

[SupportedOSPlatform("ios13.0")]
[Register("AppDelegate")]
public sealed class AppDelegate : AvaloniaAppDelegate<MobileApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RegisterUnhandledExceptionHooks();
        PlatformStartupContext.Set(new MobilePlatformServiceRoot(PlatformKind.Ios));
        return base.CustomizeAppBuilder(builder);
    }

    // 与 Android 头同型：未处理异常送入 TelemetryRuntimeService，由其按隐私开关决定是否上传。
    // iOS 没有 AndroidEnvironment.UnhandledExceptionRaiser 等价物，不拦截进程级崩溃。
    private static void RegisterUnhandledExceptionHooks()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Capture(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Capture(e.Exception);
    }

    private static void Capture(Exception exception)
    {
        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        if (telemetry is not null)
            _ = telemetry.CaptureExceptionAsync(exception);
    }
}
#endif
