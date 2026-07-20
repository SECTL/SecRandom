#if ANDROID
using Android.App;
using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using SecRandom.Core.Abstraction;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Telemetry;
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
        RegisterUnhandledExceptionHooks();
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

    // 未处理异常统一送入 TelemetryRuntimeService；其内部按隐私开关决定是否真正上传。
    // 钩子在 Host 建立前也可能触发，因此使用 IAppHost.TryGetService 惰性解析。
    private static void RegisterUnhandledExceptionHooks()
    {
        AndroidEnvironment.UnhandledExceptionRaiser += (_, e) =>
        {
            Capture(e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Capture(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Capture(e.Exception);
        };
    }

    private static void Capture(Exception exception)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(24))
            global::Android.Util.Log.Error("SecRandom.Mobile", exception.ToString());

        TelemetryRuntimeService? telemetry = IAppHost.TryGetService<TelemetryRuntimeService>();
        if (telemetry is not null)
            _ = telemetry.CaptureExceptionAsync(exception);
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
