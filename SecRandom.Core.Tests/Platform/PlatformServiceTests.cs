using Microsoft.Extensions.DependencyInjection;
using SecRandom.Platforms;
using SecRandom.Platforms.Abstractions;
using System.Diagnostics;

namespace SecRandom.Core.Tests.Platform;

public class PlatformServiceTests
{
    [Fact]
    public void StubReturnsUnsupportedForWindowFeatures()
    {
        var result = PlatformServiceRootStub.Instance.WindowFeatures.Apply(
            default,
            new WindowFeatureRequest(WindowFeatures.Topmost | WindowFeatures.SkipTaskSwitcher, true));

        Assert.Equal(WindowFeatureApplyStatus.Unsupported, result.Status);
        Assert.Equal(WindowFeatures.Topmost | WindowFeatures.SkipTaskSwitcher, result.Features);
        Assert.Equal(WindowFeatures.None, result.AppliedFeatures);
        Assert.Equal(WindowFeatures.Topmost | WindowFeatures.SkipTaskSwitcher, result.UnsupportedFeatures);
        Assert.Equal(WindowFeatures.None, result.FailedFeatures);
    }

    [Fact]
    public void PartialResultPreservesEachFeatureOutcome()
    {
        var result = WindowFeatureApplyResult.Partial(
            WindowFeatures.Topmost,
            WindowFeatures.ToolWindow,
            WindowFeatures.ExcludeFromCapture,
            "capture protection is unavailable");

        Assert.Equal(WindowFeatureApplyStatus.Failed, result.Status);
        Assert.False(result.IsComplete);
        Assert.Equal(WindowFeatures.Topmost, result.AppliedFeatures);
        Assert.Equal(WindowFeatures.ToolWindow, result.UnsupportedFeatures);
        Assert.Equal(WindowFeatures.ExcludeFromCapture, result.FailedFeatures);
        Assert.Equal(
            WindowFeatures.Topmost | WindowFeatures.ToolWindow | WindowFeatures.ExcludeFromCapture,
            result.Features);
    }

    [Fact]
    public void RegistrationUsesTheSuppliedPlatformRoot()
    {
        var services = new ServiceCollection();
        var root = PlatformServiceRootStub.Instance;
        services.AddPlatformServices(root);

        using var provider = services.BuildServiceProvider();

        Assert.Same(root, provider.GetRequiredService<IPlatformServiceRoot>());
        Assert.Same(root.WindowFeatures, provider.GetRequiredService<IWindowFeatureService>());
        Assert.Same(root.RemovableStorage, provider.GetRequiredService<IRemovableStorageCatalog>());
        Assert.Same(root.RemovableStorageBindingMarker,
            provider.GetRequiredService<IRemovableStorageBindingMarker>());
        Assert.Same(root.Capabilities, provider.GetRequiredService<PlatformCapabilities>());
        Assert.Equal(PlatformKind.Unknown, root.Kind);
        Assert.False(root.Capabilities.SupportsSingleView);
    }

    [Fact]
    public void ProcessRunnerStopsACommandBeforeWaitingForItsOutput()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add("ping 127.0.0.1 -n 6 > nul");
        }
        else
        {
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add("sleep 2");
        }

        var timer = Stopwatch.StartNew();
        var completed = PlatformProcessRunner.TryGetOutput(startInfo, TimeSpan.FromMilliseconds(100), out _);

        Assert.False(completed);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
    }
}
