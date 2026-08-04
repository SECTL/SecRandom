using System;
using Avalonia;
using Avalonia.Media;
using SecRandom.Services.CrashRecovery;
using SecRandom.Services.Desktop;
using SecRandom.Platforms;
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
        if (!UiAccessStartup.ShouldContinue(args))
        {
            Environment.ExitCode = UiAccessStartup.BootstrapExitCode;
            return;
        }

        args = UiAccessStartup.GetApplicationArguments(args);
        ConfigurePlatformServices();
        ProtocolActivation.SetStartupArguments(args);
        CrashRecoveryRuntime.SetStartupArguments(args);
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
            .LogToTrace();
    }
}
