#if IOS
using Avalonia;
using Avalonia.iOS;
using Foundation;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
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
        PlatformStartupContext.Set(new MobilePlatformServiceRoot(PlatformKind.Ios));
        return base.CustomizeAppBuilder(builder);
    }
}
#endif
