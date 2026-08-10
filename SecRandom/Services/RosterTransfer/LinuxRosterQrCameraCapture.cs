using OpenCvSharp;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// Linux V4L2 capture loop used when the desktop camera control has no Linux provider.
/// It forwards JPEG frames in memory only; no camera frame is persisted.
/// </summary>
public sealed class LinuxRosterQrCameraCapture : IAsyncDisposable
{
    private readonly VideoCapture _capture = new(0, VideoCaptureAPIs.V4L2);
    private CancellationTokenSource? _cancellation;
    private Task? _loop;

    public Task StartAsync(Func<byte[], Task> onFrame, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        if (!_capture.IsOpened())
            throw new InvalidOperationException("No V4L2 camera is available.");

        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(async () =>
        {
            using var frame = new Mat();
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    if (_capture.Read(frame) && !frame.Empty() && Cv2.ImEncode(".jpg", frame, out var jpeg))
                        await onFrame(jpeg).ConfigureAwait(false);
                    await Task.Delay(120, _cancellation.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);
        return Task.CompletedTask;
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
