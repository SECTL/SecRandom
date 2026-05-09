using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using SecRandom.Core.Models.Camera;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Camera;
using SecRandom.Core.Services.Config;

namespace SecRandom.Views.MainPages;

[PageInfo("main.cameraPreviewTest", "\uE722", useFullWidth: true, hidePageTitle: true)]
public partial class CameraPreviewTestPage : UserControl
{
    private readonly CameraDrawEngine _cameraDrawEngine = new();
    private ComboBox? _cameraComboBox;
    private TextBlock? _emptyPreviewTextBlock;
    private TextBlock? _faceCountTextBlock;
    private TextBlock? _frameInfoTextBlock;
    private NumericUpDown? _inputHeightBox;
    private NumericUpDown? _inputWidthBox;
    private bool _isLoadingConfig;
    private bool _isPickingMode;
    private int _lastFaceCount;
    private long _lastFrameId;
    private Button? _modeButton;
    private ComboBox? _modelComboBox;
    private TextBlock? _modeTextBlock;
    private NumericUpDown? _pickingSecondsBox;
    private CancellationTokenSource? _previewCancellation;
    private CameraPreviewSurface? _previewSurface;
    private Task? _previewTask;
    private ComboBox? _resolutionComboBox;
    private Button? _startStopButton;
    private TextBlock? _statusTextBlock;

    public CameraPreviewTestPage()
    {
        InitializeComponent();
        _cameraDrawEngine.FrameReady += CameraDrawEngineOnFrameReady;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _cameraComboBox = this.FindControl<ComboBox>("CameraComboBox");
        _resolutionComboBox = this.FindControl<ComboBox>("ResolutionComboBox");
        _modelComboBox = this.FindControl<ComboBox>("ModelComboBox");
        _inputWidthBox = this.FindControl<NumericUpDown>("InputWidthBox");
        _inputHeightBox = this.FindControl<NumericUpDown>("InputHeightBox");
        _pickingSecondsBox = this.FindControl<NumericUpDown>("PickingSecondsBox");
        _previewSurface = this.FindControl<CameraPreviewSurface>("PreviewSurface");
        _emptyPreviewTextBlock = this.FindControl<TextBlock>("EmptyPreviewTextBlock");
        _statusTextBlock = this.FindControl<TextBlock>("StatusTextBlock");
        _faceCountTextBlock = this.FindControl<TextBlock>("FaceCountTextBlock");
        _frameInfoTextBlock = this.FindControl<TextBlock>("FrameInfoTextBlock");
        _modeTextBlock = this.FindControl<TextBlock>("ModeTextBlock");
        _startStopButton = this.FindControl<Button>("StartStopButton");
        _modeButton = this.FindControl<Button>("ModeButton");

        EnsureCameraConfig();
        LoadConfigurationOptions();
        UpdateModeTexts();
    }

    private async void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _cameraDrawEngine.FrameReady -= CameraDrawEngineOnFrameReady;
        await StopPreviewAsync();
        _previewSurface?.Clear();
    }

    private async void StartStopButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_previewCancellation == null)
            await StartPreviewAsync();
        else
            await StopPreviewAsync();
    }

    private void ModeButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _isPickingMode = !_isPickingMode;
        _previewSurface?.SetPickingMode(_isPickingMode);
        UpdateModeTexts();
    }

    private void CameraComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingConfig || _cameraComboBox?.SelectedItem is not CameraOption camera)
            return;

        var config = GetFaceDetectorSettingsConfig();
        config.CameraSource = camera.Source;
        PopulateResolutionOptions(camera.Source, config);
        if (_resolutionComboBox?.SelectedItem is ResolutionOption resolution)
            config.CameraDisplayResolutionMap[camera.Source] = $"{resolution.Width}x{resolution.Height}";
    }

    private void ResolutionComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingConfig || _resolutionComboBox?.SelectedItem is not ResolutionOption resolution)
            return;

        var config = GetFaceDetectorSettingsConfig();
        if (!string.IsNullOrWhiteSpace(config.CameraSource))
            config.CameraDisplayResolutionMap[config.CameraSource] = $"{resolution.Width}x{resolution.Height}";
    }

    private void ModelComboBox_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingConfig || _modelComboBox?.SelectedItem is not string model)
            return;

        GetFaceDetectorSettingsConfig().DetectorType = model;
    }

    private void InputSizeBox_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isLoadingConfig || _inputWidthBox?.Value is null || _inputHeightBox?.Value is null)
            return;

        var config = GetFaceDetectorSettingsConfig();
        config.ModelInputWidth = decimal.ToInt32(_inputWidthBox.Value.Value);
        config.ModelInputHeight = decimal.ToInt32(_inputHeightBox.Value.Value);
    }

    private void PickingSecondsBox_OnValueChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_isLoadingConfig || _pickingSecondsBox?.Value is null)
            return;

        GetFaceDetectorSettingsConfig().PickingDurationSeconds = decimal.ToInt32(_pickingSecondsBox.Value.Value);
    }

    private async Task StartPreviewAsync()
    {
        EnsureCameraConfig();

        _previewCancellation = new CancellationTokenSource();
        _startStopButton!.Content = "停止预览";
        _statusTextBlock!.Text = "正在启动摄像头...";

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
        _startStopButton!.Content = "开始预览";
        _statusTextBlock!.Text = "预览已停止";

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
        _lastFrameId = packet.FrameId;
        _previewSurface?.SetFrame(packet, _isPickingMode);
        if (_emptyPreviewTextBlock != null)
            _emptyPreviewTextBlock.IsVisible = false;
        _statusTextBlock!.Text = packet.State switch
        {
            DetectionState.HasFaces => "检测正常，已框选人脸",
            DetectionState.NoFace => "检测正常，暂无人脸",
            DetectionState.Error => "检测器返回错误，仍继续显示画面",
            _ => "正在显示摄像头画面"
        };
        UpdateModeTexts();
    }

    private void UpdateModeTexts()
    {
        if (_modeTextBlock != null)
            _modeTextBlock.Text = _isPickingMode ? "抽取模式" : "预览模式";
        if (_faceCountTextBlock != null)
            _faceCountTextBlock.Text = $"检测到 {_lastFaceCount} 人";
        if (_frameInfoTextBlock != null)
            _frameInfoTextBlock.Text = _lastFrameId <= 0 ? "等待帧数据" : $"Frame #{_lastFrameId}";
        if (_modeButton != null)
            _modeButton.Content = _isPickingMode ? $"切换到预览（{_lastFaceCount} 人）" : $"切换到抽取（{_lastFaceCount} 人）";
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
    }

    private void LoadConfigurationOptions()
    {
        var config = GetFaceDetectorSettingsConfig();
        _isLoadingConfig = true;
        try
        {
            var devices = CameraDrawEngine.GetAllCameraDevices().ToList();
            var cameras = devices.Select(device => new CameraOption(device.Name, device.Source)).ToList();
            _cameraComboBox!.ItemsSource = cameras;
            _cameraComboBox.SelectedItem = cameras.FirstOrDefault(item => item.Source == config.CameraSource) ??
                                           cameras.FirstOrDefault();

            PopulateResolutionOptions(config.CameraSource, config);

            var models = ListDetectorModels().ToList();
            _modelComboBox!.ItemsSource = models;
            _modelComboBox.SelectedItem =
                models.FirstOrDefault(model => model == config.DetectorType) ?? models.FirstOrDefault();

            _inputWidthBox!.Value = config.ModelInputWidth;
            _inputHeightBox!.Value = config.ModelInputHeight;
            _pickingSecondsBox!.Value = config.PickingDurationSeconds;
        }
        finally
        {
            _isLoadingConfig = false;
        }
    }

    private void PopulateResolutionOptions(string cameraSource, FaceDetectorSettingsConfig config)
    {
        var device = CameraDrawEngine.GetAllCameraDevices().FirstOrDefault(item => item.Source == cameraSource);
        var resolutions = device?.Resolutions
            .Select(item => new ResolutionOption(item.Width, item.Height, item.PixelFormat, item.Fps))
            .OrderByDescending(item => item.Width * item.Height)
            .ThenByDescending(item => item.Fps)
            .ToList() ?? [];

        _resolutionComboBox!.ItemsSource = resolutions;
        config.CameraDisplayResolutionMap.TryGetValue(cameraSource, out var selectedResolutionText);
        _resolutionComboBox.SelectedItem =
            resolutions.FirstOrDefault(item => $"{item.Width}x{item.Height}" == selectedResolutionText) ??
            resolutions.FirstOrDefault(item => item.Width == 640 && item.Height == 480) ??
            resolutions.FirstOrDefault();
    }

    private static IEnumerable<string> ListDetectorModels()
    {
        var folders = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "data", "cv_models"),
            FindWorkspaceModelDirectory()
        };

        return folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            .SelectMany(folder => Directory.GetFiles(folder!, "*.onnx"))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name);
    }

    private static string? FindWorkspaceModelDirectory()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "data", "cv_models");
            if (Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static FaceDetectorSettingsConfig GetFaceDetectorSettingsConfig()
    {
        object handler = IAppHost.GetService<MainConfigHandler>();
        var config = handler.GetType().GetProperty("Data")?.GetValue(handler);
        var property = config?.GetType().GetProperty(nameof(FaceDetectorSettingsConfig));
        if (property?.GetValue(config) is FaceDetectorSettingsConfig value)
            return value;

        throw new InvalidOperationException(nameof(FaceDetectorSettingsConfig));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private sealed record CameraOption(string Name, string Source)
    {
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? Source : Name;
        }
    }

    private sealed record ResolutionOption(int Width, int Height, string PixelFormat, double Fps)
    {
        public string Text => $"{Width}x{Height} · {PixelFormat} · {Fps:0.#} FPS";

        public override string ToString()
        {
            return Text;
        }
    }
}

public sealed class CameraPreviewSurface : Control
{
    private static readonly SolidColorBrush PreviewBoxFill = new(Color.FromArgb(48, 0, 180, 255));
    private static readonly SolidColorBrush PickBoxFill = new(Color.FromArgb(58, 255, 169, 0));
    private static readonly Pen PreviewBoxPen = new(new SolidColorBrush(Color.FromRgb(0, 180, 255)), 3);
    private static readonly Pen PickBoxPen = new(new SolidColorBrush(Color.FromRgb(255, 169, 0)), 4);

    private WriteableBitmap? _bitmap;
    private FaceBox[] _faces = [];
    private int _frameHeight;
    private int _frameWidth;
    private bool _isPickingMode;

    public void SetFrame(CameraFramePacket packet, bool isPickingMode)
    {
        _isPickingMode = isPickingMode;
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

    public void SetPickingMode(bool isPickingMode)
    {
        _isPickingMode = isPickingMode;
        InvalidateVisual();
    }

    public void Clear()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _faces = [];
        _frameWidth = 0;
        _frameHeight = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(new SolidColorBrush(Color.FromRgb(16, 20, 24)), Bounds);

        if (_bitmap == null || _frameWidth <= 0 || _frameHeight <= 0)
            return;

        var imageRect = GetContainRect(Bounds.Size, _frameWidth, _frameHeight);
        context.DrawImage(_bitmap, imageRect);

        var scale = imageRect.Width / _frameWidth;
        var fill = _isPickingMode ? PickBoxFill : PreviewBoxFill;
        var pen = _isPickingMode ? PickBoxPen : PreviewBoxPen;

        foreach (var face in _faces)
        {
            var rect = new Rect(
                imageRect.X + face.X1 * scale,
                imageRect.Y + face.Y1 * scale,
                Math.Max(1, (face.X2 - face.X1) * scale),
                Math.Max(1, (face.Y2 - face.Y1) * scale));
            context.DrawRectangle(fill, pen, rect, 10);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _bitmap?.Dispose();
        _bitmap = null;
        base.OnDetachedFromVisualTree(e);
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