using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Attributes;
using SecRandom.Core.Enums;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Icons;
using SecRandom.Core.Models.Camera;
using SecRandom.Core.Models.SubConfigs.Picking;
using SecRandom.Core.Services.Camera;
using SecRandom.Core.Services.Config;
using FaceDrawResources = SecRandom.Langs.MainPages.FaceDraw.Resources;

namespace SecRandom.Views.MainPages;

[PageInfo("main.faceDraw", FluentIcons.VideoPersonSparkleFilled, location: PageLocation.Bottom, useFullWidth: true, hidePageTitle: true)]
public partial class CameraPreviewTestPage : UserControl
{
    private readonly CameraDrawEngine _cameraDrawEngine;
    private TextBlock? _emptyPreviewTextBlock;
    private TextBlock? _faceCountTextBlock;
    private Button? _increasePickCountButton;
    private Button? _decreasePickCountButton;
    private bool _isPicking;
    private int _lastFaceCount;
    private FaceBox[] _latestFaces = [];
    private int _pickCount = 1;
    private TextBlock? _pickCountTextBlock;
    private DispatcherTimer? _pickingTimer;
    private DispatcherTimer? _resultTimer;
    private CancellationTokenSource? _previewCancellation;
    private CameraPreviewSurface? _previewSurface;
    private Task? _previewTask;
    private Button? _startPickButton;
    private TextBlock? _statusTextBlock;
    private bool _isFrameSubscribed;
    private bool _isResultVisible;
    private bool _isSettingsSubscribed;

    public CameraPreviewTestPage()
    {
        InitializeComponent();
        _cameraDrawEngine = IAppHost.GetService<CameraDrawEngine>();
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (!_isFrameSubscribed)
        {
            _cameraDrawEngine.FrameReady += CameraDrawEngineOnFrameReady;
            _isFrameSubscribed = true;
        }

        if (!_isSettingsSubscribed)
        {
            GetFaceDetectorSettingsConfig().PropertyChanged += FaceDetectorSettingsOnPropertyChanged;
            _isSettingsSubscribed = true;
        }

        _previewSurface = this.FindControl<CameraPreviewSurface>("PreviewSurface");
        _emptyPreviewTextBlock = this.FindControl<TextBlock>("EmptyPreviewTextBlock");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");
        _faceCountTextBlock = this.FindControl<TextBlock>("RecognizedCountPanel");
        _pickCountTextBlock = this.FindControl<TextBlock>("PickCountTextBlock");
        _decreasePickCountButton = this.FindControl<Button>("DecreasePickCountButton");
        _increasePickCountButton = this.FindControl<Button>("IncreasePickCountButton");
        _startPickButton = this.FindControl<Button>("StartPickButton");
        _pickingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _pickingTimer.Tick += PickingTimer_OnTick;
        _resultTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _resultTimer.Tick += ResultTimer_OnTick;

        ApplyPreviewMode();
        _previewSurface?.SetPickerFrameColor(GetFaceDetectorSettingsConfig().PickerFrameColor);
        UpdatePickControls();
        _ = StartPreviewAsync();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        StopPicking();
        _resultTimer?.Stop();
        if (_isFrameSubscribed)
        {
            _cameraDrawEngine.FrameReady -= CameraDrawEngineOnFrameReady;
            _isFrameSubscribed = false;
        }
        if (_isSettingsSubscribed)
        {
            GetFaceDetectorSettingsConfig().PropertyChanged -= FaceDetectorSettingsOnPropertyChanged;
            _isSettingsSubscribed = false;
        }
        await StopPreviewAsync();
        _previewSurface?.Clear();
    }

    private void DecreasePickCountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _pickCount = Math.Max(1, _pickCount - 1);
        UpdatePickControls();
    }

    private void IncreasePickCountButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _pickCount = Math.Min(Math.Max(1, _lastFaceCount), _pickCount + 1);
        UpdatePickControls();
    }

    private void StartPickButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_isPicking)
        {
            StopPicking();
            return;
        }

        if (_latestFaces.Length == 0)
        {
            _statusTextBlock!.Text = FaceDrawResources.M_NoFace;
            return;
        }

        _isPicking = true;
        _isResultVisible = false;
        _pickingStartedUtc = DateTime.UtcNow;
        _startPickButton!.Content = FaceDrawResources.C_Stop;
        _statusTextBlock!.Text = FaceDrawResources.M_Picking;
        _previewSurface?.SetResultFaces([]);
        _previewSurface?.SetPickingFaces([]);
        _pickingTimer!.Start();
    }

    private void PickingTimer_OnTick(object? sender, EventArgs e)
    {
        if (!_isPicking || _latestFaces.Length == 0)
            return;

        var count = Math.Min(_pickCount, _latestFaces.Length);
        var selected = _latestFaces
            .OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue))
            .Take(count)
            .ToArray();
        _previewSurface?.SetPickingFaces(selected);

        var duration = TimeSpan.FromSeconds(Math.Clamp(GetFaceDetectorSettingsConfig().PickingDurationSeconds, 1, 60));
        if (_pickingStartedUtc + duration <= DateTime.UtcNow)
        {
            _pickingTimer!.Stop();
            _isPicking = false;
            _isResultVisible = true;
            _startPickButton!.Content = FaceDrawResources.C_Start;
            _previewSurface?.SetPickingFaces([]);
            _previewSurface?.SetResultFaces(selected);
            _statusTextBlock!.Text = string.Format(FaceDrawResources.M_PickedFormat, selected.Length);
            _resultTimer!.Start();
        }
    }

    private DateTime _pickingStartedUtc;

    private async Task StartPreviewAsync()
    {
        if (_previewCancellation != null)
            return;

        EnsureCameraConfig();

        _previewCancellation = new CancellationTokenSource();
        _statusTextBlock!.Text = FaceDrawResources.M_Starting;

        var token = _previewCancellation.Token;
        _previewTask = Task.Run(() => _cameraDrawEngine.StartPreviewAsync(token), token);
        await Task.Yield();
    }

    private async Task StopPreviewAsync()
    {
        var cancellation = _previewCancellation;
        if (cancellation == null)
            return;

        _previewCancellation = null;
        _statusTextBlock!.Text = FaceDrawResources.M_Stopped;

        await _cameraDrawEngine.StopPreviewAsync();
        await cancellation.CancelAsync();

        try
        {
            if (_previewTask != null)
                await _previewTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
            _previewTask = null;
        }
    }

    private void CameraDrawEngineOnFrameReady(object? sender, CameraFramePacket packet)
    {
        Dispatcher.UIThread.Post(() => UpdateFrame(packet));
    }

    private void UpdateFrame(CameraFramePacket packet)
    {
        _lastFaceCount = packet.Faces.Count;
        _latestFaces = packet.Faces.ToArray();
        _previewSurface?.SetFrame(packet);
        if (_emptyPreviewTextBlock != null)
            _emptyPreviewTextBlock.IsVisible = false;
        if (!_isResultVisible)
            _statusTextBlock!.Text = packet.State switch
            {
                DetectionState.HasFaces => _isPicking ? FaceDrawResources.M_Picking : FaceDrawResources.M_Ready,
                DetectionState.NoFace => FaceDrawResources.M_NoFace,
                DetectionState.Error => FaceDrawResources.M_DetectorUnavailable,
                _ => FaceDrawResources.M_Ready
            };
        UpdatePickControls();
    }

    private void ApplyPreviewMode()
    {
        var isRecognizeMode = GetFaceDetectorSettingsConfig().CameraPreviewMode == CameraPreviewMode.Recognize;
        _faceCountTextBlock!.IsVisible = isRecognizeMode;
        this.FindControl<Control>("PickCountPanel")!.IsVisible = !isRecognizeMode;
        if (isRecognizeMode)
            _statusTextBlock!.Text = FaceDrawResources.M_Recognizing;
    }

    private void FaceDetectorSettingsOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FaceDetectorSettingsConfig.CameraPreviewMode))
        {
            Dispatcher.UIThread.Post(() =>
            {
                StopPicking();
                ApplyPreviewMode();
                UpdatePickControls();
            });
        }
        else if (e.PropertyName == nameof(FaceDetectorSettingsConfig.PickerFrameColor))
        {
            var color = GetFaceDetectorSettingsConfig().PickerFrameColor;
            Dispatcher.UIThread.Post(() => _previewSurface?.SetPickerFrameColor(color));
        }
    }

    private void UpdatePickControls()
    {
        if (_faceCountTextBlock != null)
            _faceCountTextBlock.Text = string.Format(FaceDrawResources.M_FaceCountFormat, _lastFaceCount);
        _pickCount = Math.Clamp(_pickCount, 1, Math.Max(1, _lastFaceCount));
        if (_pickCountTextBlock != null)
            _pickCountTextBlock.Text = _pickCount.ToString();
        if (_decreasePickCountButton != null)
            _decreasePickCountButton.IsEnabled = !_isPicking && _pickCount > 1;
        if (_increasePickCountButton != null)
            _increasePickCountButton.IsEnabled = !_isPicking && _pickCount < _lastFaceCount;
        if (_startPickButton != null)
            _startPickButton.IsEnabled = _isPicking || _lastFaceCount > 0;
    }

    private void StopPicking()
    {
        _pickingTimer?.Stop();
        _isPicking = false;
        _isResultVisible = false;
        if (_startPickButton != null)
            _startPickButton.Content = FaceDrawResources.C_Start;
        _previewSurface?.SetPickingFaces([]);
        _previewSurface?.SetResultFaces([]);
        UpdatePickControls();
    }

    private void ResultTimer_OnTick(object? sender, EventArgs e)
    {
        _resultTimer!.Stop();
        _isResultVisible = false;
        _previewSurface?.SetResultFaces([]);
        _statusTextBlock!.Text = FaceDrawResources.M_Ready;
    }

    private static void EnsureCameraConfig()
    {
        var config = GetFaceDetectorSettingsConfig();
        if (!string.IsNullOrWhiteSpace(config.CameraSource) &&
            config.CameraDisplayResolutionMap.ContainsKey(config.CameraSource))
            return;

        var device = CameraDrawEngine.GetAllCameraDevices().FirstOrDefault();
        if (device == null)
            return;

        var resolution = device.Resolutions
            .OrderByDescending(item => item.Width == 640 && item.Height == 480)
            .ThenBy(item => Math.Abs(item.Width - 640) + Math.Abs(item.Height - 480))
            .FirstOrDefault();
        if (resolution == null)
            return;

        config.CameraSource = device.Source;
        config.CameraDisplayResolutionMap[device.Source] = $"{resolution.Width}x{resolution.Height}";
        IAppHost.GetService<MainConfigHandler>().Save();
    }

    private static FaceDetectorSettingsConfig GetFaceDetectorSettingsConfig()
    {
        var handler = IAppHost.GetService<MainConfigHandler>();
        return handler.Data.FaceDetectorSettings;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}

public sealed class CameraPreviewSurface : Control
{
    private WriteableBitmap? _bitmap;
    private FaceBox[] _faces = [];
    private int _frameHeight;
    private int _frameWidth;
    private FaceBox[] _pickingFaces = [];
    private Color _pickerFrameColor = Color.FromRgb(0, 120, 212);
    private WriteableBitmap? _resultBitmap;
    private int _resultFrameHeight;
    private int _resultFrameWidth;
    private FaceBox[] _resultFaces = [];

    public void SetFrame(CameraFramePacket packet)
    {
        _frameWidth = packet.Width;
        _frameHeight = packet.Height;
        _faces = packet.Faces.ToArray();

        if (_bitmap == null || _bitmap.PixelSize.Width != packet.Width || _bitmap.PixelSize.Height != packet.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(packet.Width, packet.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        using (var framebuffer = _bitmap.Lock())
        {
            CopyBgraBuffer(packet.BgraBuffer, framebuffer, packet.Width, packet.Height);
        }

        InvalidateVisual();
    }

    public void SetPickingFaces(IReadOnlyList<FaceBox> faces)
    {
        _pickingFaces = faces.ToArray();
        InvalidateVisual();
    }

    public void SetResultFaces(IReadOnlyList<FaceBox> faces)
    {
        _resultFaces = faces.ToArray();
        _resultBitmap?.Dispose();
        _resultBitmap = _resultFaces.Length > 0 ? CopyCurrentBitmap() : null;
        _resultFrameWidth = _resultBitmap == null ? 0 : _frameWidth;
        _resultFrameHeight = _resultBitmap == null ? 0 : _frameHeight;
        InvalidateVisual();
    }

    public void SetPickerFrameColor(Color color)
    {
        _pickerFrameColor = color;
        InvalidateVisual();
    }

    public void Clear()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _resultBitmap?.Dispose();
        _resultBitmap = null;
        _faces = [];
        _pickingFaces = [];
        _resultFaces = [];
        _frameWidth = 0;
        _frameHeight = 0;
        _resultFrameWidth = 0;
        _resultFrameHeight = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(16, 20, 24)), Bounds);

        if (_bitmap == null || _frameWidth <= 0 || _frameHeight <= 0)
            return;

        var imageRect = GetContainRect(Bounds.Size, _frameWidth, _frameHeight);

        if (_resultFaces.Length > 0)
        {
            RenderResultFaces(context);
            return;
        }

        context.DrawImage(_bitmap, imageRect);

        var scale = imageRect.Width / _frameWidth;
        var previewFill = new SolidColorBrush(Color.FromArgb(48, _pickerFrameColor.R, _pickerFrameColor.G, _pickerFrameColor.B));
        var pickFill = new SolidColorBrush(Color.FromArgb(80, _pickerFrameColor.R, _pickerFrameColor.G, _pickerFrameColor.B));
        var previewPen = new Pen(new SolidColorBrush(_pickerFrameColor), 3);
        var pickPen = new Pen(new SolidColorBrush(_pickerFrameColor), 4);
        foreach (var face in _faces)
        {
            var rect = new Rect(
                imageRect.X + face.X1 * scale,
                imageRect.Y + face.Y1 * scale,
                Math.Max(1, (face.X2 - face.X1) * scale),
                Math.Max(1, (face.Y2 - face.Y1) * scale));
            context.DrawRectangle(previewFill, previewPen, rect, 10);
        }

        foreach (var face in _pickingFaces)
        {
            var rect = new Rect(
                imageRect.X + face.X1 * scale,
                imageRect.Y + face.Y1 * scale,
                Math.Max(1, (face.X2 - face.X1) * scale),
                Math.Max(1, (face.Y2 - face.Y1) * scale));
            context.DrawRectangle(pickFill, pickPen, rect, 10);
        }
    }

    private void RenderResultFaces(DrawingContext context)
    {
        if (_resultBitmap == null || _resultFrameWidth <= 0 || _resultFrameHeight <= 0)
            return;

        var columns = (int)Math.Ceiling(Math.Sqrt(_resultFaces.Length));
        var rows = (int)Math.Ceiling(_resultFaces.Length / (double)columns);
        const double spacing = 12;
        var cellWidth = Math.Max(1, (Bounds.Width - spacing * (columns + 1)) / columns);
        var cellHeight = Math.Max(1, (Bounds.Height - spacing * (rows + 1)) / rows);

        for (var index = 0; index < _resultFaces.Length; index++)
        {
            var row = index / columns;
            var column = index % columns;
            var destination = new Rect(
                spacing + column * (cellWidth + spacing),
                spacing + row * (cellHeight + spacing),
                cellWidth,
                cellHeight);
            var face = _resultFaces[index];
            var source = GetExpandedFaceRect(face, _resultFrameWidth, _resultFrameHeight);
            context.DrawImage(_resultBitmap, source, destination);
        }
    }

    private Rect GetExpandedFaceRect(FaceBox face, int frameWidth, int frameHeight)
    {
        var width = Math.Max(1, face.X2 - face.X1);
        var height = Math.Max(1, face.Y2 - face.Y1);
        var left = Math.Max(0, face.X1 - width * 0.3);
        var top = Math.Max(0, face.Y1 - height * 0.45);
        var right = Math.Min(frameWidth, face.X2 + width * 0.3);
        var bottom = Math.Min(frameHeight, face.Y2 + height * 0.2);
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _resultBitmap?.Dispose();
        _resultBitmap = null;
        base.OnDetachedFromVisualTree(e);
    }

    private WriteableBitmap? CopyCurrentBitmap()
    {
        if (_bitmap == null || _frameWidth <= 0 || _frameHeight <= 0)
            return null;

        var copy = new WriteableBitmap(
            new PixelSize(_frameWidth, _frameHeight),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);
        using var source = _bitmap.Lock();
        using var target = copy.Lock();
        var row = new byte[_frameWidth * 4];
        for (var y = 0; y < _frameHeight; y++)
        {
            Marshal.Copy(IntPtr.Add(source.Address, y * source.RowBytes), row, 0, row.Length);
            Marshal.Copy(row, 0, IntPtr.Add(target.Address, y * target.RowBytes), row.Length);
        }

        return copy;
    }

    private static Rect GetContainRect(Size bounds, int width, int height)
    {
        var scale = Math.Min(bounds.Width / width, bounds.Height / height);
        var imageWidth = width * scale;
        var imageHeight = height * scale;
        return new Rect((bounds.Width - imageWidth) / 2, (bounds.Height - imageHeight) / 2, imageWidth, imageHeight);
    }

    private static void CopyBgraBuffer(byte[] source, ILockedFramebuffer target, int width, int height)
    {
        var sourceStride = width * 4;
        if (target.RowBytes == sourceStride)
        {
            Marshal.Copy(source, 0, target.Address, Math.Min(source.Length, sourceStride * height));
            return;
        }

        for (var row = 0; row < height; row++)
        {
            var sourceOffset = row * sourceStride;
            var targetAddress = IntPtr.Add(target.Address, row * target.RowBytes);
            Marshal.Copy(source, sourceOffset, targetAddress, Math.Min(sourceStride, source.Length - sourceOffset));
        }
    }
}
