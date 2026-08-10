using Avalonia.Threading;
using CameraView;

namespace SecRandom.Services.RosterTransfer;

/// <summary>
/// CameraView implementation for the platforms that ship its native provider: Windows, Android, and iOS.
/// </summary>
public sealed class CameraViewRosterQrCameraCapture(CameraViewControl cameraControl) : IRosterQrCameraCapture
{
    private static readonly TimeSpan CaptureInterval = TimeSpan.FromMilliseconds(60);
    private readonly CameraViewControl _cameraControl = cameraControl;
    private CancellationTokenSource? _cancellation;
    private Func<byte[], Task>? _onFrame;
    private bool _cameraInitialized;
    private bool _disposed;

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
            await CaptureNextFrameAsync(cancellation.Token);
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
        _cameraControl.PhotoCaptured -= CameraControl_OnPhotoCaptured;
        _cameraControl.CameraError -= CameraControl_OnCameraError;

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
            if (ReferenceEquals(cancellation, _cancellation) && !cancellation.IsCancellationRequested)
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

    private void CameraControl_OnCameraError(object? sender, string error)
    {
        if (_cancellation is { IsCancellationRequested: false })
            RosterQrCameraDispatcher.DispatchError(CameraError, this, error);
    }

    private async Task CaptureNextFrameAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(CaptureInterval, cancellationToken);
        if (!cancellationToken.IsCancellationRequested && _cancellation is { } active &&
            active.Token == cancellationToken)
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
