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
/// Identifies one of the two camera sources exposed by the QR import UI.
/// CameraView maps these to rear/front cameras; OpenCV maps them to device indexes zero/one.
/// </summary>
public enum RosterQrCameraSelection
{
    First,
    Second
}

/// <summary>
/// UI-ready camera choice for QR imports.
/// </summary>
public sealed record RosterQrCameraOption(RosterQrCameraSelection Selection, string Label);

/// <summary>
/// Creates the active platform's roster QR camera capture session.
/// </summary>
public interface IRosterQrCameraCaptureFactory
{
    bool IsPreviewSupported { get; }

    bool SupportsCameraSelection { get; }

    IRosterQrCameraCapture Create(Control previewControl, RosterQrCameraSelection selection);
}

/// <summary>
/// Keeps native camera provider selection in the composition layer instead of list-import views.
/// </summary>
public sealed class RosterQrCameraCaptureFactory(IPlatformServiceRoot platform) : IRosterQrCameraCaptureFactory
{
    private readonly PlatformKind _platformKind = platform?.Kind ?? throw new ArgumentNullException(nameof(platform));

    public bool IsPreviewSupported => true;

    public bool SupportsCameraSelection => _platformKind != PlatformKind.Ios;

    public IRosterQrCameraCapture Create(Control previewControl, RosterQrCameraSelection selection)
    {
        ArgumentNullException.ThrowIfNull(previewControl);

        return _platformKind switch
        {
            PlatformKind.Linux => new OpenCvRosterQrCameraCapture(VideoCaptureAPIs.V4L2, "Linux V4L2", (int)selection),
            PlatformKind.MacOs => new OpenCvRosterQrCameraCapture(VideoCaptureAPIs.AVFOUNDATION, "macOS AVFoundation", (int)selection),
            _ => new CameraViewRosterQrCameraCapture((previewControl as RosterQrCameraPreview)?.GetOrCreateCameraView()
                ?? throw new ArgumentException("The active camera provider requires a roster camera preview host.",
                    nameof(previewControl)), selection)
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
