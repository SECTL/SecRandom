using Avalonia.Controls;
using Avalonia.Threading;
using OpenCvSharp;
using SecRandom.Controls;
using SecRandom.Platforms.Abstractions;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Provides in-memory frames from the camera used by roster QR imports.
/// </summary>
public interface IRosterQrCameraCapture : IAsyncDisposable
{
    event EventHandler<string>? CameraError;

    Task<RosterQrCameraStartResult> StartAsync(Func<byte[], Task> onFrame, CancellationToken cancellationToken);
}

public enum RosterQrCameraStartResult
{
    Started,
    PermissionDenied
}

/// <summary>
/// UI-ready camera choice for QR imports.
/// </summary>
public sealed record RosterQrCameraOption(PlatformCameraDevice Device)
{
    public string Label => Device.DisplayName;
}

/// <summary>
/// Creates the active platform's roster QR camera capture session.
/// </summary>
public interface IRosterQrCameraCaptureFactory
{
    bool IsPreviewSupported { get; }

    Task<IReadOnlyList<RosterQrCameraOption>> GetAvailableOptionsAsync(CancellationToken cancellationToken);

    IRosterQrCameraCapture Create(Control previewControl, PlatformCameraDevice? device);
}

/// <summary>
/// Keeps native camera provider selection in the composition layer instead of list-import views.
/// </summary>
public sealed class RosterQrCameraCaptureFactory(IPlatformServiceRoot platform,
    IPlatformCameraDeviceCatalog cameraDevices) : IRosterQrCameraCaptureFactory
{
    private readonly PlatformKind _platformKind = platform?.Kind ?? throw new ArgumentNullException(nameof(platform));
    private readonly IPlatformCameraDeviceCatalog _cameraDevices = cameraDevices ??
        throw new ArgumentNullException(nameof(cameraDevices));

    public bool IsPreviewSupported => true;

    public async Task<IReadOnlyList<RosterQrCameraOption>> GetAvailableOptionsAsync(CancellationToken cancellationToken)
    {
        if (_platformKind == PlatformKind.Ios)
            return [];

        return (await _cameraDevices.GetAvailableAsync(cancellationToken))
            .Select(device => new RosterQrCameraOption(device))
            .ToArray();
    }

    public IRosterQrCameraCapture Create(Control previewControl, PlatformCameraDevice? device)
    {
        ArgumentNullException.ThrowIfNull(previewControl);

        return _platformKind switch
        {
            PlatformKind.Windows when device is not null => new OpenCvRosterQrCameraCapture(VideoCaptureAPIs.DSHOW,
                "Windows DirectShow", device.CaptureIndex),
            PlatformKind.Linux when device is not null => new OpenCvRosterQrCameraCapture(VideoCaptureAPIs.V4L2,
                "Linux V4L2", device.CaptureIndex),
            PlatformKind.MacOs when device is not null => new OpenCvRosterQrCameraCapture(VideoCaptureAPIs.AVFOUNDATION,
                "macOS AVFoundation", device.CaptureIndex),
            _ => new CameraViewRosterQrCameraCapture((previewControl as RosterQrCameraPreview)?.GetOrCreateCameraView()
                ?? throw new ArgumentException("The active camera provider requires a roster camera preview host.",
                    nameof(previewControl)), device?.Facing ?? PlatformCameraFacing.Default)
        };
    }
}

internal static class RosterQrCameraDispatcher
{
    public static Task DispatchFrameAsync(Func<byte[], Task> onFrame, byte[] frame)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ArgumentNullException.ThrowIfNull(frame);

        return Dispatcher.UIThread.CheckAccess()
            ? onFrame(frame)
            : Dispatcher.UIThread.InvokeAsync(() => onFrame(frame));
    }

    public static void DispatchError(EventHandler<string>? handler, object sender, string error)
    {
        if (handler is null)
            return;

        if (Dispatcher.UIThread.CheckAccess())
        {
            handler(sender, error);
            return;
        }

        Dispatcher.UIThread.Post(() => handler(sender, error));
    }
}
