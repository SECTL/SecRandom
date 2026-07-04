using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using SecRandom.Core;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Core.Services.SingleInstance;
using SecRandom.Shared;

namespace SecRandom.Services.CrashRecovery;

public static class CrashRecoveryRuntime
{
    public const string CrashReportArgument = "--crash-report";
    public const string CrashAutoRestartArgument = "--crash-auto-restart";
    public const int MaxCrashReportBytes = 1024 * 1024;

    private const string FeedbackUrl = "https://github.com/SECTL/SecRandom/issues/new";
    private static int _fatalExceptionHandled;

    public static CrashRecoveryPromptOptions? StartupPromptOptions { get; private set; }

    public static void SetStartupArguments(IReadOnlyList<string> args)
    {
        StartupPromptOptions = CrashRecoveryPromptOptions.Parse(args);
    }

    public static void ResetFatalExceptionHandledForTests()
    {
        Interlocked.Exchange(ref _fatalExceptionHandled, 0);
    }

    public static CrashRecoveryPromptOptions? TryCreateCurrentProcessPromptOptions(Exception exception)
    {
        if (StartupPromptOptions is not null)
            return null;

        CrashRecoveryMode mode = LoadConfiguredMode();
        if (mode is CrashRecoveryMode.None or CrashRecoveryMode.RestartOnly)
            return null;

        bool autoRestart = mode == CrashRecoveryMode.PromptAndRestart
                           && CrashRecoveryRestartGuard.CanBeginAutomaticRestart(
                               DefaultRestartGuardPath,
                               DateTimeOffset.UtcNow);
        return new CrashRecoveryPromptOptions(WriteCrashReport(exception), autoRestart);
    }

    public static bool TryBeginAutomaticRestart()
    {
        return CrashRecoveryRestartGuard.TryBeginAutomaticRestart(DefaultRestartGuardPath, DateTimeOffset.UtcNow);
    }

    public static bool TryHandlePromptDisplayFailure(Exception exception)
    {
        return TryHandlePromptDisplayFailure(exception, StartLaunchPlan);
    }

    public static bool TryHandlePromptDisplayFailure(Exception exception, Func<CrashRecoveryLaunchPlan, bool> startLaunchPlan)
    {
        if (Interlocked.Exchange(ref _fatalExceptionHandled, 1) != 0)
            return true;

        bool automaticRestartAllowed = CrashRecoveryRestartGuard.TryBeginAutomaticRestart(
            DefaultRestartGuardPath,
            DateTimeOffset.UtcNow);
        CrashRecoveryLaunchPlan? launchPlan = CrashRecoveryLaunchPlan.Create(
            CrashRecoveryMode.RestartOnly,
            string.Empty,
            automaticRestartAllowed);
        if (launchPlan is not null && startLaunchPlan(launchPlan))
            return true;

        Interlocked.Exchange(ref _fatalExceptionHandled, 0);
        return false;
    }

    public static bool TryHandleFatalException(Exception exception)
    {
        return TryHandleFatalException(exception, StartLaunchPlan);
    }

    public static bool TryHandleFatalException(Exception exception, Func<CrashRecoveryLaunchPlan, bool> startLaunchPlan)
    {
        if (StartupPromptOptions is not null)
            return false;

        CrashRecoveryMode mode = LoadConfiguredMode();
        if (mode == CrashRecoveryMode.None)
            return false;

        if (Interlocked.Exchange(ref _fatalExceptionHandled, 1) != 0)
            return true;

        bool automaticRestartAllowed = mode switch
        {
            CrashRecoveryMode.RestartOnly => CrashRecoveryRestartGuard.TryBeginAutomaticRestart(
                DefaultRestartGuardPath,
                DateTimeOffset.UtcNow),
            CrashRecoveryMode.PromptAndRestart => CrashRecoveryRestartGuard.CanBeginAutomaticRestart(
                DefaultRestartGuardPath,
                DateTimeOffset.UtcNow),
            _ => true
        };
        string crashReportPath = mode == CrashRecoveryMode.RestartOnly
            ? string.Empty
            : WriteCrashReport(exception);
        CrashRecoveryLaunchPlan? launchPlan = CrashRecoveryLaunchPlan.Create(mode, crashReportPath, automaticRestartAllowed);
        if (launchPlan is null && mode == CrashRecoveryMode.RestartOnly)
            launchPlan = CrashRecoveryLaunchPlan.Create(
                CrashRecoveryMode.PromptThenRestart,
                WriteCrashReport(exception),
                allowAutomaticRestart: false);
        if (launchPlan is null)
        {
            Interlocked.Exchange(ref _fatalExceptionHandled, 0);
            return false;
        }

        if (startLaunchPlan(launchPlan))
            return true;

        Interlocked.Exchange(ref _fatalExceptionHandled, 0);
        return false;
    }

    public static string ReadCrashReport(string path)
    {
        return ReadCrashReport(path, DefaultCrashReportDirectories);
    }

    public static string ReadCrashReport(string path, IReadOnlyList<string> allowedRoots)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!IsPathUnderAnyRoot(fullPath, allowedRoots))
                return ReportUnavailable(path);

            FileInfo fileInfo = new(fullPath);
            if (!fileInfo.Exists || fileInfo.Length > MaxCrashReportBytes)
                return ReportUnavailable(path);

            return File.ReadAllText(fullPath);
        }
        catch
        {
            return ReportUnavailable(path);
        }
    }

    public static ProcessStartInfo? CreateRestartStartInfo(IEnumerable<string> arguments)
    {
        return CreateRestartStartInfos(arguments).FirstOrDefault();
    }

    public static bool TryRestartCurrentApp(
        IReadOnlyList<ProcessStartInfo> startInfos,
        Func<ProcessStartInfo, bool> startProcess,
        Action requestShutdown)
    {
        return TryRestartCurrentApp(
            startInfos,
            startInfo => startProcess(startInfo) ? Process.GetCurrentProcess() : null,
            _ => true,
            requestShutdown,
            TimeSpan.Zero);
    }

    public static bool TryRestartCurrentApp(
        IReadOnlyList<ProcessStartInfo> startInfos,
        Func<ProcessStartInfo, Process?> startProcess,
        Func<Process, bool> isRestartProcessRunning,
        Action requestShutdown,
        TimeSpan startupProbeDelay)
    {
        if (!startInfos.Any(startInfo => TryStartRestartProcess(startInfo, startProcess, isRestartProcessRunning, startupProbeDelay)))
            return false;

        try
        {
            requestShutdown();
        }
        catch
        {
        }

        return true;
    }

    public static bool TryRestartCurrentApp(IReadOnlyList<ProcessStartInfo> startInfos, Action requestShutdown)
    {
        return TryRestartCurrentApp(startInfos, TryStartProcess, IsProcessRunning, requestShutdown, TimeSpan.FromMilliseconds(500));
    }

    public static IReadOnlyList<ProcessStartInfo> CreateRestartStartInfos(IEnumerable<string> arguments)
    {
        return CreateRestartStartInfos(
            Environment.ProcessPath,
            Assembly.GetEntryAssembly()?.Location,
            AppContext.BaseDirectory,
            arguments);
    }

    public static ProcessStartInfo? CreateRestartStartInfo(
        string? processPath,
        string? entryAssemblyPath,
        string? appBaseDirectory,
        IEnumerable<string> arguments)
    {
        return CreateRestartStartInfos(processPath, entryAssemblyPath, appBaseDirectory, arguments).FirstOrDefault();
    }

    public static IReadOnlyList<ProcessStartInfo> CreateRestartStartInfos(
        string? processPath,
        string? entryAssemblyPath,
        string? appBaseDirectory,
        IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return [];

        string[] restartArguments = [..arguments];
        List<ProcessStartInfo> startInfos = [];
        string? appHostPath = ResolveDesktopAppHostPath(appBaseDirectory);
        if (appHostPath is not null)
            AddStartInfo(startInfos, CreateProcessStartInfo(appHostPath, null, appBaseDirectory, restartArguments, useShellExecute: true));

        if (!IsDotnetHost(processPath) && !IsDllPath(processPath))
            AddStartInfo(startInfos, CreateProcessStartInfo(processPath, null, appBaseDirectory, restartArguments, useShellExecute: true));

        string? appAssemblyArgument = IsDllPath(processPath)
            ? processPath
            : ResolveEntryAssemblyPath(entryAssemblyPath, appBaseDirectory);
        if (appAssemblyArgument is not null)
            AddStartInfo(startInfos, CreateProcessStartInfo(
                IsDotnetHost(processPath) ? processPath : "dotnet",
                appAssemblyArgument,
                appBaseDirectory ?? Path.GetDirectoryName(appAssemblyArgument),
                restartArguments,
                useShellExecute: false));

        return startInfos;
    }

    private static ProcessStartInfo CreateProcessStartInfo(
        string fileName,
        string? appAssemblyArgument,
        string? appBaseDirectory,
        IReadOnlyList<string> arguments,
        bool useShellExecute)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            UseShellExecute = useShellExecute
        };

        string? workingDirectory = ResolveWorkingDirectory(fileName, appBaseDirectory);
        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;

        if (appAssemblyArgument is not null)
            startInfo.ArgumentList.Add(appAssemblyArgument);

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static void AddStartInfo(List<ProcessStartInfo> startInfos, ProcessStartInfo startInfo)
    {
        if (startInfos.Any(existing => HasSameLaunchCommand(existing, startInfo)))
            return;

        startInfos.Add(startInfo);
    }

    private static bool HasSameLaunchCommand(ProcessStartInfo left, ProcessStartInfo right)
    {
        return string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase)
               && left.ArgumentList.SequenceEqual(right.ArgumentList);
    }

    public static string TryWriteCrashReport(Exception exception, IReadOnlyList<string> crashReportDirectories)
    {
        string report = CreateCrashReport(exception);
        string fileName = $"crash-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.txt";

        foreach (string directory in crashReportDirectories)
        {
            try
            {
                Directory.CreateDirectory(directory);
                string path = Path.Combine(Path.GetFullPath(directory), fileName);
                File.WriteAllText(path, report);
                return path;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    public static void OpenFeedbackIssue(string crashReport)
    {
        string title = Uri.EscapeDataString(Langs.CrashRecovery.Resources.FeedbackTitle);
        string body = Uri.EscapeDataString(CreateFeedbackBody(crashReport));
        OpenUri($"{FeedbackUrl}?title={title}&body={body}");
    }

    private static CrashRecoveryMode LoadConfiguredMode()
    {
        try
        {
            MainConfigModel fallback = new();
            string path = fallback.ConfigFilePath;
            if (!File.Exists(path))
                return fallback.General.CrashRecovery.Mode;

            string json = File.ReadAllText(path);
            MainConfigModel? config = JsonSerializer.Deserialize<MainConfigModel>(json, ConfigServiceBase.JsonOptions);
            return config?.General.CrashRecovery.Mode ?? fallback.General.CrashRecovery.Mode;
        }
        catch
        {
            return CrashRecoveryMode.PromptAndRestart;
        }
    }

    private static string WriteCrashReport(Exception exception)
    {
        return TryWriteCrashReport(exception, DefaultCrashReportDirectories);
    }

    private static string CreateCrashReport(Exception exception)
    {
        StringBuilder builder = new();
        builder.AppendLine("SecRandom Crash Report");
        builder.AppendLine($"Time: {DateTimeOffset.Now:O}");
        builder.AppendLine($"Version: {GlobalConstants.VersionLong}");
        builder.AppendLine($"Process: {Environment.ProcessId}");
        builder.AppendLine();
        AppendException(builder, exception, 0);
        return builder.ToString();
    }

    private static void AppendException(StringBuilder builder, Exception exception, int depth)
    {
        builder.AppendLine($"Exception {depth}: {exception.GetType().FullName}");
        builder.AppendLine(exception.Message);
        builder.AppendLine(exception.StackTrace ?? Langs.CrashRecovery.Resources.NoStackTrace);

        if (exception.InnerException is null)
            return;

        builder.AppendLine();
        AppendException(builder, exception.InnerException, depth + 1);
    }

    private static bool StartLaunchPlan(CrashRecoveryLaunchPlan launchPlan)
    {
        SingleInstanceService.Instance.Dispose();
        return TryRestartCurrentApp(CreateRestartStartInfos(launchPlan.Arguments), static () => { });
    }

    private static bool TryStartRestartProcess(
        ProcessStartInfo startInfo,
        Func<ProcessStartInfo, Process?> startProcess,
        Func<Process, bool> isRestartProcessRunning,
        TimeSpan startupProbeDelay)
    {
        Process? process = startProcess(startInfo);
        if (process is null)
            return false;

        if (startupProbeDelay > TimeSpan.Zero)
            process.WaitForExit((int)startupProbeDelay.TotalMilliseconds);

        return isRestartProcessRunning(process);
    }

    private static Process? TryStartProcess(ProcessStartInfo startInfo)
    {
        try
        {
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProcessRunning(Process process)
    {
        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> DefaultCrashReportDirectories =>
    [
        Utils.GetDirectoryPath("crashes"),
        Path.Combine(Path.GetTempPath(), "SecRandom", "crashes")
    ];

    private static string DefaultRestartGuardPath =>
        Path.Combine(Path.GetTempPath(), "SecRandom", "crash-restart-guard.json");

    private static bool IsDotnetHost(string processPath)
    {
        return string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveEntryAssemblyPath(string? entryAssemblyPath, string? appBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(entryAssemblyPath))
            return entryAssemblyPath;

        if (string.IsNullOrWhiteSpace(appBaseDirectory))
            return null;

        string fallbackPath = Path.Combine(appBaseDirectory, DesktopAssemblyFileName);
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }

    public const string DesktopAssemblyFileName = "SecRandom.Desktop.dll";

    public static string DesktopExecutableFileName => OperatingSystem.IsWindows()
        ? "SecRandom.Desktop.exe"
        : "SecRandom.Desktop";

    private static string? ResolveDesktopAppHostPath(string? appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
            return null;

        string platformAppHostPath = Path.Combine(appBaseDirectory, DesktopExecutableFileName);
        if (File.Exists(platformAppHostPath))
            return platformAppHostPath;

        string fallbackAppHostPath = Path.Combine(appBaseDirectory, "SecRandom.Desktop.exe");
        return File.Exists(fallbackAppHostPath) ? fallbackAppHostPath : null;
    }

    private static string? ResolveWorkingDirectory(string fileName, string? appBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(appBaseDirectory))
            return appBaseDirectory;

        return Path.GetDirectoryName(fileName);
    }

    private static bool IsDllPath(string processPath)
    {
        return string.Equals(Path.GetExtension(processPath), ".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathUnderAnyRoot(string fullPath, IReadOnlyList<string> allowedRoots)
    {
        foreach (string root in allowedRoots)
        {
            if (IsPathUnderRoot(fullPath, root))
                return true;
        }

        return false;
    }

    private static bool IsPathUnderRoot(string fullPath, string root)
    {
        string fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
            fullRoot += Path.DirectorySeparatorChar;

        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return fullPath.StartsWith(fullRoot, comparison);
    }

    private static string ReportUnavailable(string path)
    {
        return string.Format(Langs.CrashRecovery.Resources.ReportUnavailable, path);
    }

    private static void OpenUri(string uri)
    {
        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            Process.Start(new ProcessStartInfo("xdg-open", uri) { UseShellExecute = false });
            return;
        }

        if (OperatingSystem.IsMacOS())
            Process.Start(new ProcessStartInfo("open", uri) { UseShellExecute = false });
    }

    private static string CreateFeedbackBody(string crashReport)
    {
        string trimmedReport = crashReport.Length > 6000 ? crashReport[..6000] : crashReport;
        return string.Format(Langs.CrashRecovery.Resources.FeedbackBody, trimmedReport);
    }
}
