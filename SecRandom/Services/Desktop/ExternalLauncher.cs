using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SecRandom.Mobile;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Services.Desktop;

public interface IExternalLauncher
{
    bool TryOpenPath(string path);
    bool TryOpenUri(string uri);
}

public sealed class ExternalLauncher(IPlatformServiceRoot platform, ILogger<ExternalLauncher> logger) : IExternalLauncher
{
    public bool TryOpenPath(string path)
    {
        if (platform is MobilePlatformServiceRoot { PathLauncher: { } mobilePathLauncher })
            return mobilePathLauncher(path);

        return TryStart(path, isUri: false);
    }

    public bool TryOpenUri(string uri) => TryStart(uri, isUri: true);

    private bool TryStart(string target, bool isUri)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Start("open", target);
            }
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsFreeBSD())
            {
                Start("xdg-open", target);
            }
            else
            {
                logger.LogWarning("当前平台不支持打开{TargetType}: {Target}", isUri ? "链接" : "路径", target);
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "打开{TargetType}失败: {Target}", isUri ? "链接" : "路径", target);
            return false;
        }
    }

    private static void Start(string executable, string target)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = false };
        startInfo.ArgumentList.Add(target);
        Process.Start(startInfo);
    }
}
