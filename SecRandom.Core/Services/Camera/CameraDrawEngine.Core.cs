using FlashCap;
using OpenCvSharp;
using SecRandom.Core.Models.Camera;
using System.Globalization;
using System.Resources;
using System.Timers;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private static readonly ResourceManager camera_resources = new(
        "SecRandom.Core.Services.Camera.CameraDrawEngine.Resources",
        typeof(CameraDrawEngine).Assembly);

    private CaptureDevice? captureDevice;
    private Timer? emitTimer;
    private Mat? latestFrame;
    private readonly object frameLock = new();
    private readonly TimeSpan emitInterval = TimeSpan.FromMilliseconds(50);
    private long frameCounter = 0;
    private int isEmittingFrame = 0;
    private Guid currentSessionId = Guid.NewGuid();
    private VideoCharacteristics? currentConfig;

    public async Task StartPreviewAsync(CancellationToken ct)
    {
        if (isPreviewRunning)
            return;

        await workerLoop(ct);
    }

    public async Task StopPreviewAsync()
    {
        isPreviewRunning = false;
        await cleanupCameraResources();
    }

    private async Task workerLoop(CancellationToken ct)
    {
        isPreviewRunning = true;

        try
        {
            await initializeCamera(ct);

            while (isPreviewRunning && !ct.IsCancellationRequested)
            {
                if (requireCameraRestart)
                {
                    requireCameraRestart = false;
                    await restartCamera(ct);
                }

                if (requireDetectorReload)
                {
                    requireDetectorReload = false;
                    await ReloadDetector();
                }

                await Task.Delay(100, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            isPreviewRunning = false;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Camera worker loop failed.");
        }
        finally
        {
            await cleanupCameraResources();
            isPreviewRunning = false;
        }
    }

    private async Task initializeCamera(CancellationToken ct)
    {
        logger.LogInformation(
            "Initializing camera preview: target={CameraSource}, resolution={Resolution}",
            targetCameraSource,
            currentCameraResolution);

        var descriptor = getFirstDescriptorByName(targetCameraSource);
        if (descriptor == null)
        {
            logger.LogError("Camera not found: {CameraSource}", targetCameraSource);
            throw new InvalidOperationException(cameraText("CameraNotFound", targetCameraSource));
        }

        logger.LogDebug("Found camera descriptor: {CameraName}", descriptor.Name);
        var (width, height) = parseResolution(currentCameraResolution ?? "");
        logger.LogDebug("Resolved camera resolution: {Width}x{Height}", width, height);

        var config = descriptor.Characteristics.FirstOrDefault(c =>
            c.Width == width && c.Height == height);

        if (config == null)
        {
            logger.LogError("Unsupported camera resolution: {Resolution}", currentCameraResolution);
            throw new InvalidOperationException(
                cameraText("ResolutionNotSupported", currentCameraResolution ?? string.Empty, targetCameraSource));
        }

        currentConfig = config;
        currentSessionId = Guid.NewGuid();
        frameCounter = 0;

        logger.LogDebug("Opening camera device.");
        captureDevice = await descriptor.OpenAsync(config, TranscodeFormats.Auto, OnFrameArrived);
        logger.LogDebug("Starting camera capture.");
        await captureDevice.StartAsync(ct);

        emitTimer = new Timer(emitInterval.TotalMilliseconds);
        emitTimer.Elapsed += OnEmitTimerElapsed;
        emitTimer.AutoReset = false;
        emitTimer.Start();
        logger.LogDebug("Camera emit timer started: interval={IntervalMilliseconds}ms", emitInterval.TotalMilliseconds);
    }

    private async Task restartCamera(CancellationToken ct)
    {
        await cleanupCameraResources();
        reloadDetector();
        await Task.Delay(100, ct);
        await initializeCamera(ct);
    }

    private Task ReloadDetector()
    {
        reloadDetector();
        return Task.CompletedTask;
    }

    private async Task cleanupCameraResources()
    {
        if (emitTimer != null)
        {
            emitTimer.Stop();
            emitTimer.Elapsed -= OnEmitTimerElapsed;
            emitTimer.Dispose();
            emitTimer = null;
        }

        if (captureDevice != null)
        {
            try
            {
                await captureDevice.StopAsync();
                captureDevice.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to dispose camera resources.");
            }
            finally
            {
                captureDevice = null;
            }
        }

        lock (frameLock)
        {
            latestFrame?.Dispose();
            latestFrame = null;
        }

        reloadDetector();
    }

    private void OnFrameArrived(PixelBufferScope bufferScope)
    {
        try
        {
            if (currentConfig == null)
            {
                return;
            }

            var imageSegment = bufferScope.Buffer.ReferImage();

            // FlashCap 转码后返回 DIB 位图（BMP 格式）
            // 对于 JPEG：自动解码为 RGB24 DIB
            // 对于 YUYV：转码为 RGB24 DIB
            // DIB 格式：BITMAPFILEHEADER (14 bytes) + BITMAPINFOHEADER (40 bytes) + pixel data

            int dibHeaderSize = 14 + 40; // BMP file header + info header
            int pixelDataOffset = imageSegment.Offset + dibHeaderSize;
            int pixelDataSize = imageSegment.Count - dibHeaderSize;

            int expectedSize = currentConfig.Width * currentConfig.Height * 3;

            // DIB 位图是 BGR 格式，底部到顶部存储（倒置）
            Mat bgrMat = Cv2.ImDecode(new Span<byte>(imageSegment.Array!, imageSegment.Offset, imageSegment.Count).ToArray(), ImreadModes.Color);

            lock (frameLock)
            {
                latestFrame?.Dispose();
                latestFrame = bgrMat;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process camera frame.");
        }
    }

    private void OnEmitTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref isEmittingFrame, 1, 0) != 0)
        {
            logger.LogWarning("Skipping frame emission because the previous frame is still processing.");
            return;
        }

        Mat? frameToProcess = null;

        try
        {
            lock (frameLock)
            {
                if (latestFrame != null && !latestFrame.IsDisposed)
                {
                    frameToProcess = latestFrame.Clone();
                }
                else
                {
                    logger.LogWarning("Skipping frame emission because the latest frame is unavailable.");
                }
            }

            if (frameToProcess == null)
                return;

            using var bgraFrame = new Mat();
            Cv2.CvtColor(frameToProcess, bgraFrame, ColorConversionCodes.BGR2BGRA);

            byte[] bgraBuffer = new byte[bgraFrame.Total() * bgraFrame.ElemSize()];
            System.Runtime.InteropServices.Marshal.Copy(
                bgraFrame.Data,
                bgraBuffer,
                0,
                bgraBuffer.Length
            );

            var (faces, detectionState) = detectFaces(frameToProcess);

            var packet = new CameraFramePacket(
                SessionId: currentSessionId,
                FrameId: Interlocked.Increment(ref frameCounter),
                Width: bgraFrame.Width,
                Height: bgraFrame.Height,
                Faces: faces,
                State: detectionState,
                BgraBuffer: bgraBuffer
            );

            OnFrameReady(packet);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to emit camera frame.");
        }
        finally
        {
            frameToProcess?.Dispose();
            Interlocked.Exchange(ref isEmittingFrame, 0);
            if (isPreviewRunning && emitTimer != null)
                emitTimer.Start();
        }
    }

    private static string cameraText(string name, params object[] args)
    {
        var format = camera_resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
        return args.Length == 0 ? format : string.Format(CultureInfo.CurrentCulture, format, args);
    }
}
