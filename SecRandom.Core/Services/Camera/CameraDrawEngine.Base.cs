using Microsoft.Extensions.Logging;
using SecRandom.Core.Abstraction;
using SecRandom.Core.Enums.Configs;
using SecRandom.Core.Models.Camera;
using SecRandom.Core.Models.SubConfigs;
using SecRandom.Core.Services.Config;

namespace SecRandom.Core.Services.Camera;

public partial class CameraDrawEngine
{
    private readonly ILogger _logger = IAppHost.GetService<ILogger<CameraDrawEngine>>();

    public CameraDrawEngine()
    {
        ConfigData.PropertyChanged += OnSettingsChanged;
        ConfigData.CameraDisplayResolutionMap.PropertyChanged += OnResolutionMapChanged;
    }

    private FaceDetectorSettingsConfig ConfigData =>
        GetFaceDetectorSettingsConfig();

    private int TargetWidth => ConfigData.ModelInputWidth;
    private int TargetHeight => ConfigData.ModelInputHeight;
    private string TargetCameraSource => ConfigData.CameraSource;
    private CameraPreviewMode CameraPreviewMode => ConfigData.CameraPreviewMode;
    private string DetectorModel => ConfigData.DetectorType;

    private string? CurrentCameraResolution =>
        !string.IsNullOrWhiteSpace(TargetCameraSource) &&
        ConfigData.CameraDisplayResolutionMap.TryGetValue(TargetCameraSource, out var value)
            ? value
            : null;

    private bool RequireCameraRestart { get; set; }
    private bool RequireDetectorReload { get; set; }
    private bool IsPreviewRunning { get; set; }
    public event EventHandler<CameraFramePacket>? FrameReady;

    private static FaceDetectorSettingsConfig GetFaceDetectorSettingsConfig()
    {
        object handler = IAppHost.GetService<MainConfigHandler>();
        var config = handler.GetType().GetProperty("Data")?.GetValue(handler);
        if (config == null)
            throw new InvalidOperationException(CameraText("ConfigMissing", nameof(FaceDetectorSettingsConfig)));

        var property = config.GetType().GetProperty(nameof(FaceDetectorSettingsConfig));
        if (property?.GetValue(config) is FaceDetectorSettingsConfig value)
            return value;

        throw new InvalidOperationException(CameraText("ConfigMissing", nameof(FaceDetectorSettingsConfig)));
    }

    protected virtual void OnFrameReady(CameraFramePacket e)
    {
        FrameReady?.Invoke(this, e);
    }
}