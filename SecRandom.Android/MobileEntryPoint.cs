#if ANDROID
using Android.App;
using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using System.Runtime.Versioning;

[assembly: UsesPermission(Android.Manifest.Permission.RequestInstallPackages)]
[assembly: UsesPermission(Android.Manifest.Permission.Internet)]

namespace SecRandom.Mobile.Android;

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
        PlatformStartupContext.Set(new MobilePlatformServiceRoot(PlatformKind.Android)
        {
            UpdateInstaller = new AndroidUpdateInstaller(),
            StartupErrorLogger = exception =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(24))
                    global::Android.Util.Log.Error("SecRandom.Mobile", exception.ToString());
            }
        });
        return base.CustomizeAppBuilder(builder);
    }
}

[ContentProvider(["${applicationId}.updatefileprovider"], Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_paths")]
public sealed class UpdateFileProvider : global::AndroidX.Core.Content.FileProvider
{
}

[Activity(MainLauncher = true, Exported = true,
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation |
                           global::Android.Content.PM.ConfigChanges.ScreenSize)]
public sealed class MainActivity : AvaloniaMainActivity
{
}
#endif
