using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using CameraView;
using CameraView.Platforms.Android;
using SecRandom.Core;
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
        // Android 没有托管 Main，Assembly.GetEntryAssembly() 恒为 null；不显式发布头程序集时，
        // 版本元数据会回退到不含 git 信息的 SecRandom.Core 并显示为 v0.0.0.0
        GlobalConstants.SetVersionAssembly(typeof(MobileApplication).Assembly);
        RegisterUnhandledExceptionHooks();
        var screenLayout = Resources?.Configuration?.ScreenLayout ?? ScreenLayout.SizeNormal;
        var isTablet = (screenLayout & ScreenLayout.SizeMask) >= ScreenLayout.SizeLarge;
        var platform = new MobilePlatformServiceRoot(PlatformKind.Android)
        {
            UpdateInstaller = new AndroidUpdateInstaller(),
            MediaPlayer = new AndroidMobileMediaPlayer(),
            CameraDevices = new AndroidCameraDeviceCatalog(this),
            PathLauncher = AndroidDataDirectoryLauncher.TryOpenPath,
            UriLauncher = TryOpenExternalUri,
            DeviceName = Build.Model,
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

    /// <summary>
    ///     用系统浏览器打开外部链接。SECTL 授权页依赖它，没有这一步移动端无法开始登录。
    /// </summary>
    private static bool TryOpenExternalUri(string url)
    {
        try
        {
            var intent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url));
            intent.AddFlags(ActivityFlags.NewTask);
            global::Android.App.Application.Context.StartActivity(intent);
            return true;
        }
        catch (Exception exception)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
                global::Android.Util.Log.Warn("SecRandom.Mobile", $"打开链接失败：{exception.Message}");
            return false;
        }
    }
}

[ContentProvider(["${applicationId}.updatefileprovider"], Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_paths")]
public sealed class UpdateFileProvider : global::AndroidX.Core.Content.FileProvider
{
}

[Activity(MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    Theme = "@style/Theme.AppCompat.DayNight.NoActionBar",
    ConfigurationChanges = global::Android.Content.PM.ConfigChanges.Orientation |
                           global::Android.Content.PM.ConfigChanges.ScreenSize |
                           global::Android.Content.PM.ConfigChanges.UiMode)]
// SECTL 登录回调：授权页跳回 cn.sectl.secrandom.mobile://oauth/callback，
// SingleTask 保证回调落到已有实例（收到时走 OnNewIntent）而不是新建一个 Activity。
[IntentFilter([Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = MobileAuthCallbackRouter.CallbackScheme,
    DataHost = MobileAuthCallbackRouter.CallbackHost,
    DataPath = MobileAuthCallbackRouter.CallbackPath)]
[SupportedOSPlatform("android24.0")]
public sealed class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        var cameraProvider = new AndroidCameraProvider(BaseContext!);
        CameraProviderFactory.RegisterProvider(cameraProvider);
        CameraProviderFactory.RegisterOrientationFactory(
            () => new AndroidDeviceOrientationProvider(BaseContext!));
        base.OnCreate(savedInstanceState);
        CameraProviderFactory.SetAndroidActivity(this);
        // Keep the viewport stable; MobileViewHost shifts only the obscured content region.
        Window?.SetSoftInputMode(SoftInput.AdjustNothing);
        // 深链冷启动：应用是被回调拉起来的，此时 Host 还没建好，路由器会先缓存起来。
        MobileAuthCallbackRouter.DeliverFromPlatform(Intent?.DataString);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        MobileAuthCallbackRouter.DeliverFromPlatform(intent?.DataString);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[]? permissions,
        Permission[]? grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        var granted = grantResults is { Length: > 0 } && grantResults[0] == Permission.Granted;
        CameraProviderFactory.NotifyAndroidPermissionResult(granted);
    }
}
