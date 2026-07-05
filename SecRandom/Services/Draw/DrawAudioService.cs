using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SecRandom.Services.Draw;

public sealed class DrawAudioService(ILogger<DrawAudioService> logger)
{
    public Task PlayAsync(string path, int volume, int fadeIn, int fadeOut, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.CompletedTask;

        if (!OperatingSystem.IsWindows())
        {
            logger.LogWarning("Draw music playback currently requires Windows MCI: {Path}", path);
            return Task.CompletedTask;
        }

        _ = PlayWindowsAsync(path, volume, cancellationToken);
        return Task.CompletedTask;
    }

    private async Task PlayWindowsAsync(string path, int volume, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var alias = "SecRandomDraw" + Guid.NewGuid().ToString("N");
                var normalizedVolume = Math.Clamp(volume, 0, 100) * 10;
                try
                {
                    MciSendString($"open \"{path}\" type mpegvideo alias {alias}", null, 0, IntPtr.Zero);
                    MciSendString($"setaudio {alias} volume to {normalizedVolume}", null, 0, IntPtr.Zero);
                    MciSendString($"play {alias} wait", null, 0, IntPtr.Zero);
                }
                finally
                {
                    MciSendString($"close {alias}", null, 0, IntPtr.Zero);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Draw music playback failed: {Path}", path);
        }
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern int MciSendString(string command, StringBuilder? returnValue, int returnLength, IntPtr hwndCallback);
}
