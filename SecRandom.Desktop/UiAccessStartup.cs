using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models;
using SecRandom.Services.CrashRecovery;

namespace SecRandom.Desktop;

internal static class UiAccessStartup
{
    private const string ElevatedBootstrapArgument = "--secrandom-uiaccess-bootstrap";
    private const int ErrorCancelled = 1223;
    private const int BootstrapTimeoutMilliseconds = 15000;
    private const uint TokenAssignPrimary = 0x0001;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenImpersonate = 0x0004;
    private const uint TokenQuery = 0x0008;
    private const uint TokenAdjustDefault = 0x0080;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint PrivilegeSetAllNecessary = 1;
    private const int TokenSessionId = 12;
    private const int TokenElevation = 20;
    private const int TokenUiAccess = 26;
    private const int SecurityAnonymous = 0;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;
    private const int TokenImpersonation = 2;
    private static readonly nint InvalidHandleValue = new(-1);
    private static readonly string DiagnosticPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecRandom",
        "uiaccess-startup.log");

    public static int BootstrapExitCode { get; private set; }

    public static bool ShouldContinue(IReadOnlyList<string> arguments)
    {
        if (!OperatingSystem.IsWindows())
            return true;

        WriteDiagnostic("Startup requested.");
        var isBootstrapProcess = arguments.Any(argument =>
            string.Equals(argument, ElevatedBootstrapArgument, StringComparison.Ordinal));
        if (!IsUiAccessRequested())
            return true;

        var appArguments = GetApplicationArguments(arguments);
        try
        {
            if (HasUiAccessToken())
            {
                WriteDiagnostic("UIAccess token already present.");
                return true;
            }

            if (isBootstrapProcess)
            {
                BootstrapExitCode = TryStartUiAccessProcess(appArguments) ? 0 : 1;
                WriteDiagnostic($"Bootstrap finished with exit code {BootstrapExitCode}.");
                return false;
            }

            if (IsElevated())
            {
                if (!TryStartUiAccessProcess(appArguments))
                    return true;

                BootstrapExitCode = 0;
                return false;
            }

            return !TryStartElevatedBootstrap(appArguments);
        }
        catch
        {
            if (isBootstrapProcess)
            {
                BootstrapExitCode = 1;
                return false;
            }

            // The process that cannot prepare UIAccess keeps the regular Topmost fallback.
            WriteDiagnostic("UIAccess preparation threw; using ordinary topmost.");
            return true;
        }
    }

    public static string[] GetApplicationArguments(IReadOnlyList<string> arguments)
    {
        return arguments.Where(argument => !IsInternalArgument(argument)).ToArray();
    }

    private static bool IsInternalArgument(string argument)
    {
        return string.Equals(argument, ElevatedBootstrapArgument, StringComparison.Ordinal);
    }

    private static bool IsUiAccessRequested()
    {
        try
        {
            MainConfigModel fallback = new();
            if (!File.Exists(fallback.ConfigFilePath))
                return false;

            var config = System.Text.Json.JsonSerializer.Deserialize<MainConfigModel>(
                File.ReadAllText(fallback.ConfigFilePath),
                ConfigServiceBase.JsonOptions);
            return config?.General.Basic.MainWindowTopmostMode == TopmostMode.UiAccess
                   || config?.FloatingWindowSettings.FloatingWindowTopmostMode == TopmostMode.UiAccess;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartElevatedBootstrap(IReadOnlyList<string> appArguments)
    {
        var bootstrapArguments = new List<string>(appArguments.Count + 1);
        bootstrapArguments.AddRange(appArguments);
        bootstrapArguments.Add(ElevatedBootstrapArgument);

        foreach (var startInfo in CrashRecoveryRuntime.CreateRestartStartInfos(bootstrapArguments))
        {
            startInfo.UseShellExecute = true;
            startInfo.Verb = "runas";
            try
            {
                using var bootstrap = Process.Start(startInfo);
                if (bootstrap is null)
                    continue;

                if (!bootstrap.WaitForExit(BootstrapTimeoutMilliseconds))
                {
                    try
                    {
                        bootstrap.Kill(entireProcessTree: true);
                        bootstrap.WaitForExit();
                    }
                    catch
                    {
                    }

                    return false;
                }

                WriteDiagnostic($"Bootstrap process exited with code {bootstrap.ExitCode}.");
                return bootstrap.ExitCode == 0;
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
            {
                return false;
            }
            catch
            {
            }
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryStartUiAccessProcess(IReadOnlyList<string> appArguments)
    {
        if (!TryCreateUiAccessToken(out var uiAccessToken))
        {
            WriteDiagnostic($"Unable to create UIAccess token: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        using (uiAccessToken)
        {
            if (uiAccessToken is null)
                return false;

            foreach (var startInfo in CrashRecoveryRuntime.CreateRestartStartInfos(appArguments))
            {
                if (!TryCreateProcessAsUser(uiAccessToken, startInfo))
                    continue;

                WriteDiagnostic("UIAccess replacement created.");
                return true;
            }
        }

        return false;
    }

    private static bool HasUiAccessToken()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
            return false;

        using (token)
        {
            return GetTokenInformationInt(token, TokenUiAccess, out var isUiAccess, sizeof(int), out _)
                   && isUiAccess != 0;
        }
    }

    private static bool IsElevated()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
            return false;

        using (token)
            return IsElevated(token);
    }

    private static bool IsElevated(SafeAccessTokenHandle token)
    {
        return GetTokenInformationInt(token, TokenElevation, out var elevated, sizeof(int), out _)
               && elevated != 0;
    }

    [SupportedOSPlatform("windows")]
    private static bool TryCreateUiAccessToken(out SafeAccessTokenHandle? uiAccessToken)
    {
        uiAccessToken = null;
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate, out var currentToken))
        {
            WriteDiagnostic($"OpenProcessToken failed: {Marshal.GetLastWin32Error()}.");
            return false;
        }

        using (currentToken)
        {
            if (!GetTokenInformationUInt(currentToken, TokenSessionId, out var sessionId, sizeof(uint), out _))
            {
                WriteDiagnostic($"GetTokenInformation(TokenSessionId) failed: {Marshal.GetLastWin32Error()}.");
                return false;
            }

            using var winlogonToken = TryDuplicateWinlogonToken(sessionId);
            if (winlogonToken is null || !SetThreadToken(nint.Zero, winlogonToken))
            {
                WriteDiagnostic($"Unable to impersonate same-session winlogon: {Marshal.GetLastWin32Error()}.");
                return false;
            }

            try
            {
                if (!DuplicateTokenEx(
                        currentToken,
                        TokenQuery | TokenDuplicate | TokenAssignPrimary | TokenAdjustDefault,
                        nint.Zero,
                        SecurityAnonymous,
                        TokenPrimary,
                        out var token))
                {
                    WriteDiagnostic($"DuplicateTokenEx failed: {Marshal.GetLastWin32Error()}.");
                    return false;
                }

                var uiAccess = 1;
                if (!SetTokenInformation(token, TokenUiAccess, ref uiAccess, sizeof(int)))
                {
                    token.Dispose();
                    WriteDiagnostic($"SetTokenInformation(TokenUIAccess) failed: {Marshal.GetLastWin32Error()}.");
                    return false;
                }

                uiAccessToken = token;
                return true;
            }
            finally
            {
                RevertToSelf();
            }
        }
    }

    private static SafeAccessTokenHandle? TryDuplicateWinlogonToken(uint sessionId)
    {
        if (!LookupPrivilegeValue(null, "SeTcbPrivilege", out var tcbPrivilege))
            return null;

        var snapshot = CreateToolhelp32Snapshot(0x00000002, 0);
        if (snapshot == nint.Zero || snapshot == InvalidHandleValue)
            return null;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (!Process32First(snapshot, ref entry))
                return null;

            do
            {
                if (!string.Equals(entry.ExecutableFile, "winlogon.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                var process = OpenProcess(ProcessQueryLimitedInformation, false, entry.ProcessId);
                if (process == nint.Zero)
                    continue;

                try
                {
                    if (!OpenProcessToken(process, TokenQuery | TokenDuplicate, out var processToken))
                        continue;

                    using (processToken)
                    {
                        var privilegeSet = new PrivilegeSet
                        {
                            PrivilegeCount = 1,
                            Control = PrivilegeSetAllNecessary,
                            Privilege = new LuidAndAttributes { Luid = tcbPrivilege }
                        };
                        if (!PrivilegeCheck(processToken, ref privilegeSet, out var hasTcbPrivilege)
                            || !hasTcbPrivilege
                            || !GetTokenInformationUInt(processToken, TokenSessionId, out var tokenSessionId, sizeof(uint), out _)
                            || tokenSessionId != sessionId)
                            continue;

                        if (DuplicateTokenEx(
                                processToken,
                                TokenImpersonate,
                                nint.Zero,
                                SecurityImpersonation,
                                TokenImpersonation,
                                out var duplicatedToken))
                            return duplicatedToken;
                    }
                }
                finally
                {
                    CloseHandle(process);
                }
            } while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return null;
    }

    private static bool TryCreateProcessAsUser(SafeAccessTokenHandle token, ProcessStartInfo startInfo)
    {
        var startupInfo = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
        GetStartupInfo(ref startupInfo);
        var commandLine = CreateCommandLine(startInfo);
        var workingDirectory = string.IsNullOrWhiteSpace(startInfo.WorkingDirectory)
            ? Environment.CurrentDirectory
            : startInfo.WorkingDirectory;

        if (!CreateProcessAsUser(
                token,
                null,
                commandLine,
                nint.Zero,
                nint.Zero,
                false,
                0,
                nint.Zero,
                workingDirectory,
                ref startupInfo,
                out var processInformation))
        {
            WriteDiagnostic($"CreateProcessAsUserW failed: {Marshal.GetLastWin32Error()}. File: {startInfo.FileName}");
            return false;
        }

        CloseHandle(processInformation.Process);
        CloseHandle(processInformation.Thread);
        return true;
    }

    private static void WriteDiagnostic(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DiagnosticPath)!);
            File.AppendAllText(DiagnosticPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static StringBuilder CreateCommandLine(ProcessStartInfo startInfo)
    {
        var commandLine = new StringBuilder(QuoteWindowsArgument(startInfo.FileName));
        foreach (var argument in startInfo.ArgumentList)
            commandLine.Append(' ').Append(QuoteWindowsArgument(argument));

        if (!string.IsNullOrWhiteSpace(startInfo.Arguments))
            commandLine.Append(' ').Append(startInfo.Arguments);

        return commandLine;
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 && argument.All(character => !char.IsWhiteSpace(character) && character != '"'))
            return argument;

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            result.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(nint processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetTokenInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationInt(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, EntryPoint = "GetTokenInformation")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformationUInt(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        out uint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateTokenEx(
        SafeAccessTokenHandle existingToken,
        uint desiredAccess,
        nint tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out SafeAccessTokenHandle newToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetTokenInformation(
        SafeAccessTokenHandle tokenHandle,
        int tokenInformationClass,
        ref int tokenInformation,
        int tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadToken(nint thread, SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RevertToSelf();

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? systemName, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrivilegeCheck(
        SafeAccessTokenHandle clientToken,
        ref PrivilegeSet requiredPrivileges,
        [MarshalAs(UnmanagedType.Bool)] out bool result);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateProcessAsUserW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessAsUser(
        SafeAccessTokenHandle token,
        string? applicationName,
        StringBuilder commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        nint environment,
        string currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32FirstW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "Process32NextW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern void GetStartupInfo(ref StartupInfo startupInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrivilegeSet
    {
        public uint PrivilegeCount;
        public uint Control;
        public LuidAndAttributes Privilege;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExecutableFile;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X;
        public uint Y;
        public uint XSize;
        public uint YSize;
        public uint XCountChars;
        public uint YCountChars;
        public uint FillAttribute;
        public uint Flags;
        public ushort ShowWindow;
        public ushort Reserved2Count;
        public nint Reserved2;
        public nint StandardInput;
        public nint StandardOutput;
        public nint StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public uint ProcessId;
        public uint ThreadId;
    }
}
