using Android.Content;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using SecRandom.Core.Abstraction;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using SecRandom.Services.Telemetry;
using System.Runtime.Versioning;
using SecRandom.Mobile;

namespace SecRandom.Android;

[Application]
[SupportedOSPlatform("android24.0")]
public class MobileApplication : AvaloniaAndroidApplication<App>
{
    protected MobileApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        RegisterUnhandledExceptionHooks();
        var screenLayout = Resources?.Configuration?.ScreenLayout ?? ScreenLayout.SizeNormal;
        var isTablet = (screenLayout & ScreenLayout.SizeMask) >= ScreenLayout.SizeLarge;
        var platform = new MobilePlatformServiceRoot(PlatformKind.Android)
        {
            UpdateInstaller = new AndroidUpdateInstaller(),
            MediaPlayer = new AndroidMobileMediaPlayer(),
            StartupErrorLogger = exception =>
            {
                if (OperatingSystem.IsAndroidVersionAtLeast(24))
                    global::Android.Util.Log.Error("SecRandom.Mobile", exception.ToString());
            },
            UsesDesktopMainView = isTablet
        };
        PlatformStartupContext.Set(platform);
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
[SupportedOSPlatform("android24.0")]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Keep the viewport stable; MobileViewHost shifts only the obscured content region.
        Window?.SetSoftInputMode(SoftInput.AdjustNothing);
    }
}
