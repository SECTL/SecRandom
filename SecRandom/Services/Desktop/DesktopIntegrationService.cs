using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.SubConfigs.General;
using SecRandom.Core.Services.Config;
using SecRandom.Services.CrashRecovery;

namespace SecRandom.Services.Desktop;

public sealed class DesktopIntegrationService(
    MainConfigHandler configHandler,
    ILogger<DesktopIntegrationService> logger)
{
    private const string ApplicationName = "SecRandom";
    private const string ProtocolScheme = "secrandom";
    private const string WindowsRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string WindowsProtocolKey = @"Software\Classes\secrandom";

    private BasicSettingsConfig Settings => configHandler.Data.General.Basic;

    public bool IsUiAccessAvailable()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            return HasUiAccessToken();
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Unable to inspect the Windows UIAccess token.");
            return false;
        }
    }

    public bool IsUiAccessRequested()
    {
        return Settings.MainWindowTopmostMode == TopmostMode.UiAccess
               || configHandler.Data.FloatingWindowSettings.FloatingWindowTopmostMode == TopmostMode.UiAccess;
    }

    public void EnsureConfiguredIntegrations()
    {
        if (IsUiAccessRequested() && !IsUiAccessAvailable())
        {
            logger.LogWarning("Configured UIAccess topmost mode is unavailable; using ordinary topmost for this session.");
        }

        if (Settings.Autostart && !TrySetAutostart(true, out var autostartError))
        {
            Settings.Autostart = false;
            configHandler.Save();
            logger.LogWarning("Unable to restore configured autostart integration: {Error}", autostartError);
        }

        if (Settings.UrlProtocol && !TrySetUrlProtocol(true, out var protocolError))
        {
            Settings.UrlProtocol = false;
            configHandler.Save();
            logger.LogWarning("Unable to restore configured URL protocol integration: {Error}", protocolError);
        }
    }

    public bool TrySetAutostart(bool enabled, out string error)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetWindowsAutostart(enabled);
            else if (OperatingSystem.IsLinux())
                SetLinuxAutostart(enabled);
            else if (OperatingSystem.IsMacOS())
                SetMacAutostart(enabled);
            else
                throw new PlatformNotSupportedException();

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            if (enabled)
                TryRemoveAutostartArtifacts();
            logger.LogWarning(ex, "Unable to {Action} autostart.", enabled ? "enable" : "disable");
            error = ex.Message;
            return false;
        }
    }

    public bool TrySetUrlProtocol(bool enabled, out string error)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetWindowsUrlProtocol(enabled);
            else if (OperatingSystem.IsLinux())
                SetLinuxUrlProtocol(enabled);
            else if (OperatingSystem.IsMacOS())
                SetMacUrlProtocol(enabled);
            else
                throw new PlatformNotSupportedException();

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            if (enabled)
                TryRemoveUrlProtocolArtifacts();
            logger.LogWarning(ex, "Unable to {Action} the URL protocol.", enabled ? "enable" : "disable");
            error = ex.Message;
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsAutostart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(WindowsRunKey, writable: true)
                        ?? throw new InvalidOperationException("Unable to access the current-user startup registry key.");
        if (enabled)
            key.SetValue(ApplicationName, CreateWindowsCommandLine([]), RegistryValueKind.String);
        else
            key.DeleteValue(ApplicationName, throwOnMissingValue: false);
    }

    [SupportedOSPlatform("windows")]
    private static bool HasUiAccessToken()
    {
        const int tokenUiAccess = 26;
        using var identity = WindowsIdentity.GetCurrent();
        return GetTokenInformation(
                   identity.AccessToken.DangerousGetHandle(),
                   tokenUiAccess,
                   out var isUiAccess,
                   sizeof(int),
                   out _)
               && isUiAccess != 0;
    }

    private static void SetLinuxAutostart(bool enabled)
    {
        var path = Path.Combine(GetXdgConfigHome(), "autostart", "secrandom.desktop");
        if (!enabled)
        {
            DeleteFileIfExists(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Join('\n',
        [
            "[Desktop Entry]",
            "Type=Application",
            "Name=SecRandom",
            $"Exec={CreateDesktopCommand([])}",
            "X-GNOME-Autostart-enabled=true"
        ]) + '\n');
    }

    [SupportedOSPlatform("macos")]
    private static void SetMacAutostart(bool enabled)
    {
        var path = Path.Combine(GetMacLaunchAgentsDirectory(), "cn.sectl.secrandom.plist");
        if (!enabled)
        {
            if (File.Exists(path) && !RunCommand("launchctl", ["unload", path], allowFailure: false))
                throw new InvalidOperationException("launchctl could not unload the user launch agent.");
            DeleteFileIfExists(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, CreateLaunchAgentPlist(GetLaunchArguments([])));
        if (!RunCommand("launchctl", ["load", path], allowFailure: true))
        {
            DeleteFileIfExists(path);
            throw new InvalidOperationException("launchctl could not load the user launch agent.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void SetWindowsUrlProtocol(bool enabled)
    {
        if (!enabled)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(WindowsProtocolKey, throwOnMissingSubKey: false);
            }
            catch (ArgumentException)
            {
            }

            return;
        }

        using var protocolKey = Registry.CurrentUser.CreateSubKey(WindowsProtocolKey, writable: true)
                                ?? throw new InvalidOperationException("Unable to access the current-user protocol registry key.");
        protocolKey.SetValue(string.Empty, "URL:SecRandom Protocol", RegistryValueKind.String);
        protocolKey.SetValue("URL Protocol", string.Empty, RegistryValueKind.String);
        using var commandKey = protocolKey.CreateSubKey(@"shell\open\command", writable: true)
                               ?? throw new InvalidOperationException("Unable to register the URL command.");
        commandKey.SetValue(string.Empty, CreateWindowsCommandLine(["--url", "%1"]), RegistryValueKind.String);
    }

    private static void SetLinuxUrlProtocol(bool enabled)
    {
        var applicationsDirectory = Path.Combine(GetXdgDataHome(), "applications");
        var desktopFileName = "secrandom-url-handler.desktop";
        var path = Path.Combine(applicationsDirectory, desktopFileName);
        if (!enabled)
        {
            RemoveLinuxProtocolAssociations(desktopFileName);
            DeleteFileIfExists(path);
            RunCommand("update-desktop-database", [applicationsDirectory], allowFailure: true);
            return;
        }

        Directory.CreateDirectory(applicationsDirectory);
        File.WriteAllText(path, string.Join('\n',
        [
            "[Desktop Entry]",
            "Type=Application",
            "Name=SecRandom URL Handler",
            $"Exec={CreateDesktopCommand(["--url", "%u"])}",
            "MimeType=x-scheme-handler/secrandom;",
            "NoDisplay=true"
        ]) + '\n');

        if (!RunCommand("xdg-mime", ["default", desktopFileName, "x-scheme-handler/secrandom"], allowFailure: true))
        {
            DeleteFileIfExists(path);
            throw new InvalidOperationException("xdg-mime could not register the secrandom URL handler.");
        }

        RunCommand("update-desktop-database", [applicationsDirectory], allowFailure: true);
    }

    [SupportedOSPlatform("macos")]
    private static void SetMacUrlProtocol(bool enabled)
    {
        var bundlePath = Path.Combine(GetMacApplicationSupportDirectory(), "SecRandom URL Handler.app");
        if (!enabled)
        {
            if (Directory.Exists(bundlePath)
                && !RunCommand(GetMacLsRegisterPath(), ["-u", bundlePath], allowFailure: false))
                throw new InvalidOperationException("LaunchServices could not unregister the secrandom URL handler.");
            DeleteDirectoryIfExists(bundlePath);
            return;
        }

        var contentsPath = Path.Combine(bundlePath, "Contents");
        var macOsPath = Path.Combine(contentsPath, "MacOS");
        Directory.CreateDirectory(macOsPath);
        File.WriteAllText(Path.Combine(contentsPath, "Info.plist"), CreateMacProtocolInfoPlist());
        var launcherPath = Path.Combine(macOsPath, "SecRandomUrlHandler");
        File.WriteAllText(launcherPath, CreateMacProtocolLauncher());
        File.SetUnixFileMode(launcherPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        if (!RunCommand(GetMacLsRegisterPath(), ["-f", bundlePath], allowFailure: true))
        {
            DeleteDirectoryIfExists(bundlePath);
            throw new InvalidOperationException("LaunchServices could not register the secrandom URL handler.");
        }
    }

    private static IReadOnlyList<string> GetLaunchArguments(IEnumerable<string> arguments)
    {
        var startInfo = CrashRecoveryRuntime.CreateRestartStartInfo(arguments)
                        ?? throw new InvalidOperationException("Unable to resolve the SecRandom launch command.");
        return [startInfo.FileName, ..startInfo.ArgumentList];
    }

    private static string CreateWindowsCommandLine(IEnumerable<string> arguments)
    {
        return string.Join(' ', GetLaunchArguments(arguments).Select(argument =>
            argument == "%1" ? "\"%1\"" : QuoteWindowsArgument(argument)));
    }

    private static string CreateDesktopCommand(IEnumerable<string> arguments)
    {
        return string.Join(' ', GetLaunchArguments(arguments).Select(argument =>
            argument == "%u" ? "%u" : QuoteDesktopArgument(argument)));
    }

    private static string CreateLaunchAgentPlist(IReadOnlyList<string> arguments)
    {
        var argumentXml = string.Concat(arguments.Select(argument => $"<string>{SecurityElement.Escape(argument)}</string>"));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
               + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
               + $"<plist version=\"1.0\"><dict><key>Label</key><string>cn.sectl.secrandom</string><key>ProgramArguments</key><array>{argumentXml}</array><key>RunAtLoad</key><true/></dict></plist>\n";
    }

    private static string CreateMacProtocolInfoPlist()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
               + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
               + $"<plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>cn.sectl.secrandom.urlhandler</string><key>CFBundleName</key><string>SecRandom URL Handler</string><key>CFBundlePackageType</key><string>APPL</string><key>CFBundleExecutable</key><string>SecRandomUrlHandler</string><key>CFBundleURLTypes</key><array><dict><key>CFBundleURLName</key><string>SecRandom URL</string><key>CFBundleURLSchemes</key><array><string>{ProtocolScheme}</string></array></dict></array></dict></plist>\n";
    }

    private static string CreateMacProtocolLauncher()
    {
        var command = string.Join(' ', GetLaunchArguments([]).Select(QuotePosixShellArgument));
        return $"#!/bin/sh\nexec {command} --url \"$1\"\n";
    }

    private static bool RunCommand(string fileName, IReadOnlyList<string> arguments, bool allowFailure)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName) { UseShellExecute = false };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            return process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch when (allowFailure)
        {
            return false;
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private static void RemoveLinuxProtocolAssociations(string desktopFileName)
    {
        var scheme = "x-scheme-handler/secrandom";
        var mimeAppsPaths = new[]
        {
            Path.Combine(GetXdgConfigHome(), "mimeapps.list"),
            Path.Combine(GetXdgDataHome(), "applications", "mimeapps.list")
        };

        foreach (var path in mimeAppsPaths)
        {
            if (!File.Exists(path))
                continue;

            var lines = File.ReadAllLines(path);
            var updatedLines = new List<string>(lines.Length);
            var changed = false;
            foreach (var line in lines)
            {
                if (!line.StartsWith($"{scheme}=", StringComparison.Ordinal))
                {
                    updatedLines.Add(line);
                    continue;
                }

                var remainingHandlers = line[(scheme.Length + 1)..]
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Where(handler => !string.Equals(handler, desktopFileName, StringComparison.Ordinal))
                    .ToArray();
                if (remainingHandlers.Length == 0)
                {
                    changed = true;
                    continue;
                }

                var updatedLine = $"{scheme}={string.Join(';', remainingHandlers)};";
                updatedLines.Add(updatedLine);
                changed |= !string.Equals(line, updatedLine, StringComparison.Ordinal);
            }

            if (changed)
                File.WriteAllLines(path, updatedLines);
        }
    }

    private static void TryRemoveAutostartArtifacts()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetWindowsAutostart(false);
            else if (OperatingSystem.IsLinux())
                SetLinuxAutostart(false);
            else if (OperatingSystem.IsMacOS())
                DeleteFileIfExists(Path.Combine(GetMacLaunchAgentsDirectory(), "cn.sectl.secrandom.plist"));
        }
        catch
        {
            // The original failure is reported to the caller; cleanup remains best effort.
        }
    }

    private static void TryRemoveUrlProtocolArtifacts()
    {
        try
        {
            if (OperatingSystem.IsWindows())
                SetWindowsUrlProtocol(false);
            else if (OperatingSystem.IsLinux())
            {
                const string desktopFileName = "secrandom-url-handler.desktop";
                RemoveLinuxProtocolAssociations(desktopFileName);
                DeleteFileIfExists(Path.Combine(GetXdgDataHome(), "applications", desktopFileName));
            }
            else if (OperatingSystem.IsMacOS())
                DeleteDirectoryIfExists(Path.Combine(GetMacApplicationSupportDirectory(), "SecRandom URL Handler.app"));
        }
        catch
        {
            // The original failure is reported to the caller; cleanup remains best effort.
        }
    }

    private static string GetXdgConfigHome()
    {
        return Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
    }

    private static string GetXdgDataHome()
    {
        return Environment.GetEnvironmentVariable("XDG_DATA_HOME")
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
    }

    private static string GetMacLaunchAgentsDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
    }

    private static string GetMacApplicationSupportDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", ApplicationName);
    }

    private static string GetMacLsRegisterPath()
    {
        return "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";
    }

    private static string QuoteDesktopArgument(string argument)
    {
        return $"\"{argument.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string QuotePosixShellArgument(string argument)
    {
        return $"'{argument.Replace("'", "'\"'\"'")}'";
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length > 0 && argument.All(character => !char.IsWhiteSpace(character) && character != '"'))
            return argument;

        var result = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
                result.Append('\\', backslashes * 2 + 1);
            else
                result.Append('\\', backslashes);

            result.Append(character);
            backslashes = 0;
        }

        result.Append('\\', backslashes * 2);
        return result.Append('"').ToString();
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [SupportedOSPlatform("windows")]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);
}
