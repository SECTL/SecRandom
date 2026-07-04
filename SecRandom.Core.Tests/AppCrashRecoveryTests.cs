namespace SecRandom.Core.Tests;

using System.Diagnostics;
using CrashRecoveryPromptOptions = global::SecRandom.Services.CrashRecovery.CrashRecoveryPromptOptions;
using CrashRecoveryRuntime = global::SecRandom.Services.CrashRecovery.CrashRecoveryRuntime;
using DispatcherCrashRecovery = global::SecRandom.Services.CrashRecovery.DispatcherCrashRecovery;

public class AppCrashRecoveryTests
{
    [Fact]
    public void DispatcherUnhandledExceptionRecovery_ShowsPromptInCurrentProcessWithoutImmediateShutdown()
    {
        var shutdownRequested = false;
        CrashRecoveryPromptOptions? shownPrompt = null;
        var promptOptions = new CrashRecoveryPromptOptions("/tmp/secrandom-crash.txt", true);

        bool handled = DispatcherCrashRecovery.TryRecover(
            new InvalidOperationException("ui crash"),
            _ => promptOptions,
            options =>
            {
                shownPrompt = options;
                return true;
            },
            _ => throw new InvalidOperationException("prompt fallback should not run"),
            _ => false,
            () => shutdownRequested = true);

        Assert.True(handled);
        Assert.Same(promptOptions, shownPrompt);
        Assert.False(shutdownRequested);
    }

    [Fact]
    public void DispatcherUnhandledExceptionRecovery_FallsBackToRestartWhenPromptDisplayThrows()
    {
        var shutdownRequested = false;
        var promptFallbackRequested = false;
        var normalFallbackRequested = false;
        var promptOptions = new CrashRecoveryPromptOptions("/tmp/secrandom-crash.txt", true);

        bool handled = DispatcherCrashRecovery.TryRecover(
            new InvalidOperationException("ui crash"),
            _ => promptOptions,
            _ => throw new InvalidOperationException("prompt failed"),
            _ => promptFallbackRequested = true,
            _ => normalFallbackRequested = true,
            () => shutdownRequested = true);

        Assert.True(handled);
        Assert.True(promptFallbackRequested);
        Assert.False(normalFallbackRequested);
        Assert.True(shutdownRequested);
    }

    [Fact]
    public void CrashPromptRestart_TriesFallbackWhenStartedProcessImmediatelyExits()
    {
        List<string> calls = [];
        ProcessStartInfo[] startInfos = [new("bad.exe"), new("good.exe")];
        Queue<bool> runningResults = new([false, true]);

        bool restarted = CrashRecoveryRuntime.TryRestartCurrentApp(
            startInfos,
            startInfo =>
            {
                calls.Add(startInfo.FileName);
                return Process.GetCurrentProcess();
            },
            _ => runningResults.Dequeue(),
            () => calls.Add("shutdown"),
            TimeSpan.Zero);

        Assert.True(restarted);
        Assert.Equal(["bad.exe", "good.exe", "shutdown"], calls);
    }
}
