using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using SecRandom;
using SecRandom.Extensions;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Desktop;
using SecRandom.Services.Plugins;
using SecRandom.Platforms;
using SecRandom.Shared;
#if SEC_RANDOM_PLATFORM_WINDOWS
using SecRandom.Platforms.Windows;
#elif SEC_RANDOM_PLATFORM_LINUX
using SecRandom.Platforms.Linux;
#elif SEC_RANDOM_PLATFORM_MACOS
using SecRandom.Platforms.MacOs;
#endif

namespace SecRandom.Desktop;

internal sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // UiAccess startup reads the persisted topmost setting, so data-root selection must precede it.
        Utils.PrepareDesktopDataRoot();
        if (!UiAccessStartup.ShouldContinue(args))
        {
            Environment.ExitCode = UiAccessStartup.BootstrapExitCode;
            return;
        }

        args = UiAccessStartup.GetApplicationArguments(args);
        ConfigurePlatformServices();
        ProtocolActivation.SetStartupArguments(args);
        CrashRecoveryRuntime.SetStartupArguments(args);
        PluginManager.SetStartupArguments(args);
        AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(CrashRecoveryPromptOptions.RemoveCrashRecoveryArguments(args));
        }
        catch (Exception exception)
        {
            if (!CrashRecoveryRuntime.TryHandleFatalException(exception))
                throw;
        }
        finally
        {
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomainOnUnhandledException;
        }
    }

    private static void ConfigurePlatformServices()
    {
#if SEC_RANDOM_PLATFORM_WINDOWS
        if (OperatingSystem.IsWindows())
            WindowsTouchKeyboardIntegration.Initialize();
        PlatformStartupContext.Set(new WindowsPlatformServiceRoot());
#elif SEC_RANDOM_PLATFORM_LINUX
        PlatformStartupContext.Set(new LinuxPlatformServiceRoot());
#elif SEC_RANDOM_PLATFORM_MACOS
        PlatformStartupContext.Set(new MacOsPlatformServiceRoot());
#else
        throw new PlatformNotSupportedException("No SecRandom desktop platform implementation was selected.");
#endif
    }

    private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            CrashRecoveryRuntime.TryHandleFatalException(exception);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://SecRandom/Assets/Fonts/MiSans/#MiSans"
            })
            .AfterPlatformServicesSetup(_ => BindAssetLoader())
            .LogToTrace()
            .LogToHostSink();
    }

    private static void BindAssetLoader()
    {
        var appAssembly = typeof(App).Assembly;
        var assemblyDirectory = Path.GetDirectoryName(appAssembly.Location);
        var assetRoot = Path.Combine(
            string.IsNullOrEmpty(assemblyDirectory) ? AppContext.BaseDirectory : assemblyDirectory,
            "Assets");

        var assetLoader = new OverlayAssetLoader(
            new StandardAssetLoader(appAssembly),
            appAssembly,
            appAssembly.GetName().Name!,
            "/Assets/",
            assetRoot,
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Updates/release-public-key.txt"
            });

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        var locatorType = typeof(AvaloniaLocator);
        var locator = locatorType.GetProperty("CurrentMutable", flags)?.GetValue(null)
                      ?? throw new InvalidOperationException("Unable to get AvaloniaLocator.CurrentMutable.");
        var bindMethod = locatorType.GetMethod("Bind", flags)?.MakeGenericMethod(typeof(IAssetLoader))
                         ?? throw new InvalidOperationException("Unable to get AvaloniaLocator.Bind<T>().");
        var registration = bindMethod.Invoke(locator, null)
                           ?? throw new InvalidOperationException("Unable to bind Avalonia IAssetLoader.");
        var toConstantMethod = registration.GetType().GetMethod("ToConstant", flags)?.MakeGenericMethod(typeof(IAssetLoader))
                               ?? throw new InvalidOperationException("Unable to get AvaloniaLocator.ToConstant<T>().");

        toConstantMethod.Invoke(registration, [assetLoader]);
    }
}
