using Avalonia.Threading;
using CameraView;
using CameraView.Services;
using SkiaSharp;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// CameraView implementation for the platforms that ship its native provider: Windows, Android, and iOS.
/// </summary>
public sealed class CameraViewRosterQrCameraCapture(CameraViewControl cameraControl) : IRosterQrCameraCapture
{
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan PreviewFrameFallbackDelay = TimeSpan.FromMilliseconds(750);
    private readonly CameraViewControl _cameraControl = cameraControl;
    private CancellationTokenSource? _cancellation;
    private Func<byte[], Task>? _onFrame;
    private ICameraProvider? _cameraProvider;
    private bool _cameraInitialized;
    private bool _disposed;
    private int _hasReceivedPreviewFrame;
    private int _isDispatchingPreviewFrame;

    public event EventHandler<string>? CameraError;

    public async Task<RosterQrCameraStartResult> StartAsync(Func<byte[], Task> onFrame,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(onFrame);
        ThrowIfDisposed();
        if (_cancellation is not null)
            throw new InvalidOperationException("The roster QR camera is already running.");

        var provider = CameraProviderFactory.Create();
        var permissions = CameraProviderFactory.CreatePermissions(provider);
        if (!await permissions.CheckPermissionAsync() && !await permissions.RequestPermissionAsync())
            return RosterQrCameraStartResult.PermissionDenied;

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation = cancellation;
        _onFrame = onFrame;
        _cameraProvider = provider;
        provider.FrameReceived += CameraProvider_OnFrameReceived;
        _cameraControl.PhotoCaptured += CameraControl_OnPhotoCaptured;
        _cameraControl.CameraError += CameraControl_OnCameraError;

        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
            await RunOnUiThreadAsync(async () =>
            {
                _cameraControl.CameraProvider = provider;
                await _cameraControl.InitializeCameraAsync(provider);
                _cameraInitialized = true;
                await _cameraControl.StartCameraAsync();
            });
            _ = StartPhotoCaptureFallbackAsync(cancellation.Token);
            return RosterQrCameraStartResult.Started;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        var cancellation = Interlocked.Exchange(ref _cancellation, null);
        cancellation?.Cancel();
        _onFrame = null;
        var provider = Interlocked.Exchange(ref _cameraProvider, null);
        provider?.FrameReceived -= CameraProvider_OnFrameReceived;
        _cameraControl.PhotoCaptured -= CameraControl_OnPhotoCaptured;
        _cameraControl.CameraError -= CameraControl_OnCameraError;
        Interlocked.Exchange(ref _isDispatchingPreviewFrame, 0);

        try
        {
            if (_cameraInitialized)
                await RunOnUiThreadAsync(_cameraControl.StopCameraAsync);
        }
        catch
        {
            // A provider may already be stopped while the import drawer is closing.
        }
        finally
        {
            _cameraInitialized = false;
            cancellation?.Dispose();
        }
    }

    private async void CameraControl_OnPhotoCaptured(object? sender, byte[] imageBytes)
    {
        var cancellation = _cancellation;
        var onFrame = _onFrame;
        if (cancellation is null || onFrame is null || cancellation.IsCancellationRequested)
            return;

        try
        {
            await RosterQrCameraDispatcher.DispatchFrameAsync(onFrame, imageBytes);
            if (ReferenceEquals(cancellation, _cancellation) && !cancellation.IsCancellationRequested &&
                Volatile.Read(ref _hasReceivedPreviewFrame) == 0)
                await CaptureNextFrameAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The drawer closed while a capture or decode was pending.
        }
        catch (Exception exception)
        {
            RosterQrCameraDispatcher.DispatchError(CameraError, this, exception.Message);
        }
    }

    private void CameraProvider_OnFrameReceived(object? sender, SKBitmap frame)
    {
        var cancellation = _cancellation;
        var onFrame = _onFrame;
        if (cancellation is null || onFrame is null || cancellation.IsCancellationRequested ||
            Interlocked.CompareExchange(ref _isDispatchingPreviewFrame, 1, 0) != 0)
        {
            return;
        }

        Volatile.Write(ref _hasReceivedPreviewFrame, 1);
        try
        {
            using var image = SKImage.FromBitmap(frame);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 80);
            if (encoded is null)
            {
                Interlocked.Exchange(ref _isDispatchingPreviewFrame, 0);
                return;
            }

            _ = DispatchPreviewFrameAsync(cancellation, onFrame, encoded.ToArray());
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(ref _isDispatchingPreviewFrame, 0);
            RosterQrCameraDispatcher.DispatchError(CameraError, this, exception.Message);
        }
    }

    private async Task DispatchPreviewFrameAsync(CancellationTokenSource cancellation,
        Func<byte[], Task> onFrame, byte[] imageBytes)
    {
        try
        {
            await RosterQrCameraDispatcher.DispatchFrameAsync(onFrame, imageBytes);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The drawer closed while the current preview frame was being decoded.
        }
        catch (Exception exception)
        {
            RosterQrCameraDispatcher.DispatchError(CameraError, this, exception.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _isDispatchingPreviewFrame, 0);
        }
    }

    private async Task StartPhotoCaptureFallbackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PreviewFrameFallbackDelay, cancellationToken);
            if (Volatile.Read(ref _hasReceivedPreviewFrame) == 0)
                await CaptureNextFrameAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The drawer closed before a fallback capture was required.
        }
        catch (Exception exception)
        {
            RosterQrCameraDispatcher.DispatchError(CameraError, this, exception.Message);
        }
    }

    private void CameraControl_OnCameraError(object? sender, string error)
    {
        if (_cancellation is { IsCancellationRequested: false })
            RosterQrCameraDispatcher.DispatchError(CameraError, this, error);
    }

    private async Task CaptureNextFrameAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _hasReceivedPreviewFrame) != 0)
            return;

        await Task.Delay(CaptureInterval, cancellationToken);
        if (!cancellationToken.IsCancellationRequested && _cancellation is { } active &&
            active.Token == cancellationToken && Volatile.Read(ref _hasReceivedPreviewFrame) == 0)
            await RunOnUiThreadAsync(_cameraControl.TakePhotoAsync);
    }

    private static Task RunOnUiThreadAsync(Func<Task> action)
    {
        return Dispatcher.UIThread.CheckAccess()
            ? action()
            : Dispatcher.UIThread.InvokeAsync(action);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CameraViewRosterQrCameraCapture));
    }
}
