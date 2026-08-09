using System.Diagnostics;

namespace SecRandom.Platforms;

internal static class PlatformProcessRunner
{
    public static bool TryGetOutput(ProcessStartInfo startInfo, TimeSpan timeout, out string output)
    {
        output = string.Empty;
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
                return false;

            using var timeoutCts = new CancellationTokenSource(timeout);
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            try
            {
                process.WaitForExitAsync(timeoutCts.Token).GetAwaiter().GetResult();
                output = outputTask.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(1_000);
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    _ = outputTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }

                return false;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
