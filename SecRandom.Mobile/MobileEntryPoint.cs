#if ANDROID
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Mobile;

[Application]
public sealed class MobileApplication : AvaloniaAndroidApplication<MobileApp>
{
    protected MobileApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        PlatformStartupContext.Set(new MobilePlatformServiceRoot(PlatformKind.Android));
        return base.CustomizeAppBuilder(builder);
    }
}

[Activity(MainLauncher = true, Exported = true,
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation |
                           Android.Content.PM.ConfigChanges.ScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
#endif

#if IOS
using Avalonia;
using Avalonia.iOS;
using Foundation;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Mobile;

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
