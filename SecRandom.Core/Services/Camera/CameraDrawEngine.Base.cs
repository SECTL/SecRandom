using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Xaml.Interactions.Custom;
using OpenCvSharp;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Camera;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;
using SecRandom.Shared.ComponentModels;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    public event EventHandler<CameraFramePacket>? FrameReady;

    private FaceDetectorSettingsConfig configData =>
        IAppHost.GetService<MainConfigHandler>().Data.FaceDetectorSettingsConfig;

    private int targetWidth => configData.ModelInputWidth;
    private int targetHeight => configData.ModelInputHeight;
    private string targetCameraSource => configData.CameraSource;
    private CameraPreviewMode cameraPreviewMode => configData.CameraPreviewMode;
    private string detectorModel => configData.DetectorType;

    private string? currentCameraResolution =>
        !string.IsNullOrWhiteSpace(targetCameraSource) &&
        configData.CameraDisplayResolutionMap.TryGetValue(targetCameraSource, out var value)
            ? value
            : null;

    private bool requireCameraRestart { get; set; }
    private bool requireDetectorReload { get; set; }
    private bool isPreviewRunning { get; set; }

    public CameraDrawEngine()
    {
        configData.PropertyChanged += OnSettingsChanged;
        configData.CameraDisplayResolutionMap.PropertyChanged += OnResolutionMapChanged;
    }

    protected virtual void OnFrameReady(CameraFramePacket e)
    {
        FrameReady?.Invoke(this, e);
    }
}
