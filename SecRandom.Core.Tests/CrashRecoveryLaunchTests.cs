namespace SecRandom.Core.Tests;

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ConfigServiceBase = global::SecRandom.Core.Abstraction.ConfigServiceBase;
using CrashRecoveryGuardState = global::SecRandom.Services.CrashRecovery.CrashRecoveryGuardState;
using CrashRecoveryLaunchPlan = global::SecRandom.Services.CrashRecovery.CrashRecoveryLaunchPlan;
using CrashRecoveryMode = global::SecRandom.Core.Enums.Configs.CrashRecoveryMode;
using CrashRecoveryPromptOptions = global::SecRandom.Services.CrashRecovery.CrashRecoveryPromptOptions;
using CrashRecoveryRestartGuard = global::SecRandom.Services.CrashRecovery.CrashRecoveryRestartGuard;
using CrashRecoveryRuntime = global::SecRandom.Services.CrashRecovery.CrashRecoveryRuntime;
using MainConfigModel = global::SecRandom.Core.Models.MainConfigModel;

public class CrashRecoveryLaunchTests
{
    [Fact]
    public void PromptAndRestart_BuildsLaunchWithCrashReportAndAutoRestartArguments()
    {
        CrashRecoveryLaunchPlan? plan = CrashRecoveryLaunchPlan.Create(
            CrashRecoveryMode.PromptAndRestart,
            "/tmp/secrandom-crash.txt");

        Assert.NotNull(plan);
        Assert.Contains(CrashRecoveryRuntime.CrashReportArgument, plan.Arguments);
        Assert.Contains("/tmp/secrandom-crash.txt", plan.Arguments);
        Assert.Contains(CrashRecoveryRuntime.CrashAutoRestartArgument, plan.Arguments);
    }

    [Fact]
    public void PromptOptions_ParseBlankCrashReportArgumentAsPromptRequest()
    {
        CrashRecoveryPromptOptions? options = CrashRecoveryPromptOptions.Parse(
            [CrashRecoveryRuntime.CrashReportArgument, string.Empty, CrashRecoveryRuntime.CrashAutoRestartArgument]);

        Assert.NotNull(options);
        Assert.Equal(string.Empty, options.CrashReportPath);
        Assert.True(options.AutoRestart);
    }

    [Fact]
    public void RestartStartInfo_AppHostRunUsesShellExecute()
    {
        var startInfo = CrashRecoveryRuntime.CreateRestartStartInfo(
            "/app/SecRandom.Desktop",
            "/app/SecRandom.Desktop.dll",
            "/app",
            [CrashRecoveryRuntime.CrashReportArgument, "/tmp/report.txt"]);

        Assert.NotNull(startInfo);
        Assert.Equal("/app/SecRandom.Desktop", startInfo.FileName);
        Assert.True(startInfo.UseShellExecute);
        Assert.Equal(CrashRecoveryRuntime.CrashReportArgument, startInfo.ArgumentList[0]);
        Assert.Equal("/tmp/report.txt", startInfo.ArgumentList[1]);
    }

    [Fact]
    public void RestartStartInfos_FrameworkDependentKeepsDotnetDllFallbackAfterAppHost()
    {
        string appBaseDirectory = Directory.CreateTempSubdirectory("secrandom-app-").FullName;
        string desktopDll = Path.Combine(appBaseDirectory, CrashRecoveryRuntime.DesktopAssemblyFileName);
        string desktopAppHost = Path.Combine(appBaseDirectory, CrashRecoveryRuntime.DesktopExecutableFileName);
        File.WriteAllText(desktopDll, string.Empty);
        File.WriteAllText(desktopAppHost, string.Empty);

        try
        {
            IReadOnlyList<ProcessStartInfo> startInfos = CrashRecoveryRuntime.CreateRestartStartInfos(
                "dotnet.exe",
                desktopDll,
                appBaseDirectory,
                [CrashRecoveryRuntime.CrashReportArgument, "/tmp/report.txt"]);

            Assert.Equal(2, startInfos.Count);
            Assert.Equal(desktopAppHost, startInfos[0].FileName);
            Assert.True(startInfos[0].UseShellExecute);
            Assert.Equal("dotnet.exe", startInfos[1].FileName);
            Assert.False(startInfos[1].UseShellExecute);
            Assert.Equal(desktopDll, startInfos[1].ArgumentList[0]);
        }
        finally
        {
            Directory.Delete(appBaseDirectory, recursive: true);
        }
    }

    [Fact]
    public void PromptAndRestartPromptLaunchDoesNotConsumeAutomaticRestartAllowance()
    {
        CrashRecoveryRuntime.ResetFatalExceptionHandledForTests();
        MainConfigModel config = new();
        string configPath = config.ConfigFilePath;
        string? originalConfig = File.Exists(configPath) ? File.ReadAllText(configPath) : null;
        string guardPath = Path.Combine(Path.GetTempPath(), "SecRandom", "crash-restart-guard.json");
        string? originalGuard = File.Exists(guardPath) ? File.ReadAllText(guardPath) : null;
        CrashRecoveryLaunchPlan? startedPlan = null;

        try
        {
            config.General.CrashRecovery.Mode = CrashRecoveryMode.PromptAndRestart;
            File.WriteAllText(configPath, JsonSerializer.Serialize(config, ConfigServiceBase.JsonOptions));
            if (File.Exists(guardPath))
                File.Delete(guardPath);

            bool handled = CrashRecoveryRuntime.TryHandleFatalException(
                new InvalidOperationException("prompt restart"),
                plan =>
                {
                    startedPlan = plan;
                    return true;
                });

            Assert.True(handled);
            Assert.NotNull(startedPlan);
            Assert.Contains(CrashRecoveryRuntime.CrashReportArgument, startedPlan.Arguments);
            Assert.Contains(CrashRecoveryRuntime.CrashAutoRestartArgument, startedPlan.Arguments);
            Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(guardPath, DateTimeOffset.UtcNow));
        }
        finally
        {
            RestoreFile(configPath, originalConfig);
            RestoreFile(guardPath, originalGuard);
            CrashRecoveryRuntime.ResetFatalExceptionHandledForTests();
        }
    }

    [Fact]
    public void PromptDisplayFailureConsumesAutomaticRestartAllowance()
    {
        CrashRecoveryRuntime.ResetFatalExceptionHandledForTests();
        string guardPath = Path.Combine(Path.GetTempPath(), "SecRandom", "crash-restart-guard.json");
        string? originalGuard = File.Exists(guardPath) ? File.ReadAllText(guardPath) : null;

        try
        {
            if (File.Exists(guardPath))
                File.Delete(guardPath);

            Assert.True(CrashRecoveryRuntime.TryHandlePromptDisplayFailure(
                new InvalidOperationException("prompt display failed"),
                _ => true));

            CrashRecoveryRuntime.ResetFatalExceptionHandledForTests();

            Assert.False(CrashRecoveryRuntime.TryHandlePromptDisplayFailure(
                new InvalidOperationException("prompt display failed again"),
                _ => true));
        }
        finally
        {
            RestoreFile(guardPath, originalGuard);
            CrashRecoveryRuntime.ResetFatalExceptionHandledForTests();
        }
    }

    [Fact]
    public void TryStartProcess_ReturnsNullWhenProcessStartThrows()
    {
        MethodInfo method = typeof(CrashRecoveryRuntime).GetMethod(
            "TryStartProcess",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var startInfo = new ProcessStartInfo("/definitely/missing/secrandom")
        {
            UseShellExecute = false
        };

        Process? started = (Process?)method.Invoke(null, [startInfo]);

        Assert.Null(started);
    }

    [Fact]
    public void RestartGuard_AllowsFirstAutomaticRestartThenBlocksRepeatedCrash()
    {
        string statePath = Path.Combine(Path.GetTempPath(), $"secrandom-guard-{Guid.NewGuid():N}.json");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        try
        {
            Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(statePath, now));
            Assert.False(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(statePath, now.AddSeconds(1)));
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [Fact]
    public void RestartGuard_CanBeginDoesNotConsumeRestartAllowance()
    {
        string statePath = Path.Combine(Path.GetTempPath(), $"secrandom-guard-{Guid.NewGuid():N}.json");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        try
        {
            Assert.True(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(statePath, now));
            Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(statePath, now));
            Assert.False(CrashRecoveryRestartGuard.CanBeginAutomaticRestart(statePath, now.AddSeconds(1)));
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    [Fact]
    public void RestartGuard_AllowsRestartAfterBackoffExpires()
    {
        string statePath = Path.Combine(Path.GetTempPath(), $"secrandom-guard-{Guid.NewGuid():N}.json");
        DateTimeOffset now = DateTimeOffset.UtcNow;

        try
        {
            File.WriteAllText(statePath, JsonSerializer.Serialize(
                new CrashRecoveryGuardState(now.AddMinutes(-10)),
                ConfigServiceBase.JsonOptions));

            Assert.True(CrashRecoveryRestartGuard.TryBeginAutomaticRestart(statePath, now));
        }
        finally
        {
            File.Delete(statePath);
        }
    }

    private static void RestoreFile(string path, string? contents)
    {
        if (contents is null)
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
