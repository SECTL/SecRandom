#if ANDROID
using Android.App;
using Android.Content;
using Android.Runtime;
using AndroidX.Core.Content;
using Avalonia;
using Avalonia.Android;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using System.Runtime.Versioning;

[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]

namespace SecRandom.Mobile;

[Application(Icon = "@mipmap/app_logo")]
[SupportedOSPlatform("android24.0")]
public class MobileApplication : AvaloniaAndroidApplication<MobileApp>
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

[ContentProvider(["${applicationId}.updatefileprovider"], Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_paths")]
public sealed class UpdateFileProvider : FileProvider
{
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
