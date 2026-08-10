using OpenCvSharp;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// OpenCV desktop capture loop used when CameraView has no native provider.
/// It uses V4L2 on Linux and AVFoundation on macOS, forwarding JPEG frames in memory only.
/// </summary>
public sealed class OpenCvRosterQrCameraCapture : IRosterQrCameraCapture
{
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(100);
    private readonly VideoCapture _capture;
    private readonly string _cameraApiName;
    private readonly int _cameraIndex;
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public event EventHandler<string>? CameraError;

    public OpenCvRosterQrCameraCapture(VideoCaptureAPIs captureApi, string cameraApiName, int cameraIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cameraApiName);
        ArgumentOutOfRangeException.ThrowIfNegative(cameraIndex);
        _cameraApiName = cameraApiName;
        _cameraIndex = cameraIndex;
        _capture = new VideoCapture(cameraIndex, captureApi);
    }

    public Task<RosterQrCameraStartResult> StartAsync(Func<byte[], Task> onFrame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (!_capture.IsOpened())
            throw new InvalidOperationException($"No {_cameraApiName} camera is available at index {_cameraIndex}.");

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        _loop = Task.Run(async () =>
        {
            using var frame = new Mat();
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    if (_capture.Read(frame) && !frame.Empty() && Cv2.ImEncode(".jpg", frame, out var jpeg))
                        await RosterQrCameraDispatcher.DispatchFrameAsync(onFrame, jpeg).ConfigureAwait(false);
                    await Task.Delay(CaptureInterval, cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) when (!cancellation.IsCancellationRequested)
            {
                RosterQrCameraDispatcher.DispatchError(CameraError, this, exception.Message);
            }
        }, CancellationToken.None);
        return Task.FromResult(RosterQrCameraStartResult.Started);
    }

    public ValueTask DisposeAsync()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _capture.Release();
        _capture.Dispose();
        return ValueTask.CompletedTask;
    }
}
