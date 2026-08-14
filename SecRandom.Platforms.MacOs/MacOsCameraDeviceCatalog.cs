using System.Diagnostics;
using System.Text.Json;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Platforms.MacOs;

public sealed class MacOsCameraDeviceCatalog : IPlatformCameraDeviceCatalog
{
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(3);

    public async Task<IReadOnlyList<PlatformCameraDevice>> GetAvailableAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
            return [];

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "/usr/sbin/system_profiler",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("SPCameraDataType");
        process.StartInfo.ArgumentList.Add("-json");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(QueryTimeout);
        try
        {
            if (!process.Start())
                return [];

            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            if (process.ExitCode != 0)
                return [];

            using var document = JsonDocument.Parse(output);
            if (!document.RootElement.TryGetProperty("SPCameraDataType", out var cameras) ||
                cameras.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return cameras.EnumerateArray()
                .Select((camera, index) => new { Name = GetName(camera), Index = index })
                .Where(camera => !string.IsNullOrWhiteSpace(camera.Name))
                .Select(camera => new PlatformCameraDevice($"avfoundation:{camera.Index}", camera.Name!, camera.Index))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? GetName(JsonElement camera) =>
        camera.TryGetProperty("_name", out var name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
}
