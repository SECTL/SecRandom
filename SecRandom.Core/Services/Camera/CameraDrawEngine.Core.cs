using System.Globalization;
using System.Resources;
using System.Runtime.InteropServices;
using System.Timers;
using FlashCap;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using SecRandom.Core.Models.Camera;
using Timer = System.Timers.Timer;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private static readonly ResourceManager CameraResources = new(
        "SecRandom.Core.Services.Camera.CameraDrawEngine.Resources",
        typeof(CameraDrawEngine).Assembly);

    private readonly TimeSpan _emitInterval = TimeSpan.FromMilliseconds(50);
    private readonly object _frameLock = new();

    private CaptureDevice? _captureDevice;
    private VideoCharacteristics? _currentConfig;
    private Guid _currentSessionId = Guid.NewGuid();
    private Timer? _emitTimer;
    private long _frameCounter;
    private int _isEmittingFrame;
    private Mat? _latestFrame;

    public async Task StartPreviewAsync(CancellationToken ct)
    {
        if (IsPreviewRunning)
            return;

        await WorkerLoop(ct);
    }

    public async Task StopPreviewAsync()
    {
        IsPreviewRunning = false;
        await CleanupCameraResources();
    }

    private async Task WorkerLoop(CancellationToken ct)
    {
        IsPreviewRunning = true;

        try
        {
            await InitializeCamera(ct);

            while (IsPreviewRunning && !ct.IsCancellationRequested)
            {
                if (RequireCameraRestart)
                {
                    RequireCameraRestart = false;
                    await RestartCamera(ct);
                }

                if (RequireDetectorReload)
                {
                    RequireDetectorReload = false;
                    await ReloadDetector();
                }

                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            IsPreviewRunning = false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Camera worker loop failed.");
        }
        finally
        {
            await CleanupCameraResources();
            IsPreviewRunning = false;
        }
    }

    private async Task InitializeCamera(CancellationToken ct)
    {
        _logger.LogInformation(
            "Initializing camera preview: target={CameraSource}, resolution={Resolution}",
            TargetCameraSource,
            CurrentCameraResolution);

        var descriptor = GetFirstDescriptorByName(TargetCameraSource);
        if (descriptor == null)
        {
            _logger.LogError("Camera not found: {CameraSource}", TargetCameraSource);
            throw new InvalidOperationException(CameraText("CameraNotFound", TargetCameraSource));
        }

        _logger.LogDebug("Found camera descriptor: {CameraName}", descriptor.Name);
        var (width, height) = ParseResolution(CurrentCameraResolution ?? "");
        _logger.LogDebug("Resolved camera resolution: {Width}x{Height}", width, height);

        var config = descriptor.Characteristics.FirstOrDefault(c =>
            c.Width == width && c.Height == height);

        if (config == null)
        {
            _logger.LogError("Unsupported camera resolution: {Resolution}", CurrentCameraResolution);
            throw new InvalidOperationException(
                CameraText("ResolutionNotSupported", CurrentCameraResolution ?? string.Empty, TargetCameraSource));
        }

        _currentConfig = config;
        _currentSessionId = Guid.NewGuid();
        _frameCounter = 0;

        _logger.LogDebug("Opening camera device.");
        _captureDevice = await descriptor.OpenAsync(config, TranscodeFormats.Auto, OnFrameArrived);
        _logger.LogDebug("Starting camera capture.");
        await _captureDevice.StartAsync(ct);

        _emitTimer = new Timer(_emitInterval.TotalMilliseconds);
        _emitTimer.Elapsed += OnEmitTimerElapsed;
        _emitTimer.AutoReset = false;
        _emitTimer.Start();
        _logger.LogDebug("Camera emit timer started: interval={IntervalMilliseconds}ms",
            _emitInterval.TotalMilliseconds);
    }

    private async Task RestartCamera(CancellationToken ct)
    {
        await CleanupCameraResources();
        _ReloadDetector();
        await Task.Delay(100, ct);
        await InitializeCamera(ct);
    }

    private Task ReloadDetector()
    {
        _ReloadDetector();
        return Task.CompletedTask;
    }

    private async Task CleanupCameraResources()
    {
        if (_emitTimer != null)
        {
            _emitTimer.Stop();
            _emitTimer.Elapsed -= OnEmitTimerElapsed;
            _emitTimer.Dispose();
            _emitTimer = null;
        }

        if (_captureDevice != null)
            try
            {
                await _captureDevice.StopAsync();
                _captureDevice.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose camera resources.");
            }
            finally
            {
                _captureDevice = null;
            }

        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }

        _ReloadDetector();
    }

    private void OnFrameArrived(PixelBufferScope bufferScope)
    {
        try
        {
            if (_currentConfig == null) return;

            var imageSegment = bufferScope.Buffer.ReferImage();

            // FlashCap 转码后返回 DIB 位图（BMP 格式）
            // 对于 JPEG：自动解码为 RGB24 DIB
            // 对于 YUYV：转码为 RGB24 DIB
            // DIB 格式：BITMAPFILEHEADER (14 bytes) + BITMAPINFOHEADER (40 bytes) + pixel data

            var dibHeaderSize = 14 + 40; // BMP file header + info header
            var pixelDataOffset = imageSegment.Offset + dibHeaderSize;
            var pixelDataSize = imageSegment.Count - dibHeaderSize;

            var expectedSize = _currentConfig.Width * _currentConfig.Height * 3;

            // DIB 位图是 BGR 格式，底部到顶部存储（倒置）
            var bgrMat =
                Cv2.ImDecode(new Span<byte>(imageSegment.Array!, imageSegment.Offset, imageSegment.Count).ToArray(),
                    ImreadModes.Color);

            lock (_frameLock)
            {
                _latestFrame?.Dispose();
                _latestFrame = bgrMat;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process camera frame.");
        }
    }

    private void OnEmitTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _isEmittingFrame, 1, 0) != 0)
        {
            _logger.LogWarning("Skipping frame emission because the previous frame is still processing.");
            return;
        }

        Mat? frameToProcess = null;

        try
        {
            lock (_frameLock)
            {
                if (_latestFrame != null && !_latestFrame.IsDisposed)
                    frameToProcess = _latestFrame.Clone();
                else
                    _logger.LogWarning("Skipping frame emission because the latest frame is unavailable.");
            }

            if (frameToProcess == null)
                return;

            using var bgraFrame = new Mat();
            Cv2.CvtColor(frameToProcess, bgraFrame, ColorConversionCodes.BGR2BGRA);

            var bgraBuffer = new byte[bgraFrame.Total() * bgraFrame.ElemSize()];
            Marshal.Copy(
                bgraFrame.Data,
                bgraBuffer,
                0,
                bgraBuffer.Length
            );

            var (faces, detectionState) = DetectFacesForPreview(frameToProcess);

            var packet = new CameraFramePacket(
                _currentSessionId,
                Interlocked.Increment(ref _frameCounter),
                bgraFrame.Width,
                bgraFrame.Height,
                faces,
                detectionState,
                bgraBuffer
            );

            OnFrameReady(packet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to emit camera frame.");
        }
        finally
        {
            frameToProcess?.Dispose();
            Interlocked.Exchange(ref _isEmittingFrame, 0);
            if (IsPreviewRunning && _emitTimer != null)
                _emitTimer.Start();
        }
    }

    private static string CameraText(string name, params object[] args)
    {
        var format = CameraResources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
