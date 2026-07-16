using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Updates;

public interface IUpdateNotificationService
{
    Task ShowUpdateAvailableAsync(string version);
}

public sealed class UpdateNotificationService(ILogger<UpdateNotificationService> logger) : IUpdateNotificationService
{
    public Task ShowUpdateAvailableAsync(string version)
    {
        try
        {
            var startInfo = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true };
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "powershell";
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add($"[Windows.UI.Notifications.ToastNotificationManager,Windows.UI.Notifications,ContentType=WindowsRuntime] | Out-Null; $xml = New-Object Windows.Data.Xml.Dom.XmlDocument; $xml.LoadXml('<toast><visual><binding template=\"ToastGeneric\"><text>SecRandom</text><text>发现新版本 {version}</text></binding></visual></toast>'); [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('SecRandom').Show((New-Object Windows.UI.Notifications.ToastNotification $xml))");
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo.FileName = "osascript";
                startInfo.ArgumentList.Add("-e");
                startInfo.ArgumentList.Add($"display notification \"发现新版本 {version.Replace("\\", "\\\\").Replace("\"", "\\\"")}\" with title \"SecRandom\"");
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                startInfo.FileName = "notify-send";
                startInfo.ArgumentList.Add("--app-name=SecRandom");
                startInfo.ArgumentList.Add("SecRandom");
                startInfo.ArgumentList.Add($"发现新版本 {version}");
            }
            else
            {
                return Task.CompletedTask;
            }

            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "无法显示系统更新通知。");
        }

        return Task.CompletedTask;
    }
}
